using System.Text;
using System.Threading.RateLimiting;
using CaneFactory.API.Auth;
using CaneFactory.API.Hubs;
using CaneFactory.API.Middleware;
using CaneFactory.API.Services;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Services;
using CaneFactory.Infrastructure.Weighing;
using CaneFactory.Infrastructure.Camera;
using CaneFactory.Infrastructure.Printing;
using CaneFactory.Infrastructure.Sms;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---- Environment variable aliases (no secrets in source / appsettings) ----
void MapEnv(string env, string key)
{
    var v = Environment.GetEnvironmentVariable(env);
    if (!string.IsNullOrWhiteSpace(v)) builder.Configuration[key] = v;
}
MapEnv("CANE_JWT_SECRET", "Jwt:Secret");
MapEnv("CANE_ENCRYPTION_KEY", "Security:EncryptionKey");
MapEnv("CANE_CONNECTION_STRING", "ConnectionStrings:Default");
MapEnv("CANE_DB_PROVIDER", "Database:Provider");
MapEnv("CANE_SEED_DEV_PASSWORD", "Seed:DeveloperPassword");
MapEnv("CANE_IMAGE_STORAGE_ROOT", "Storage:ImageRoot");
MapEnv("CANE_CAMERA_SIMULATOR", "Camera:SimulatorMode");

// ---- Database (SQL Server 2019 Express in production; SQLite fallback for dev containers) ----
var provider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var connString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default (env CANE_CONNECTION_STRING) is not configured.");
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) o.UseSqlite(connString);
    else o.UseSqlServer(connString, sql =>
    {
        // A blocked SQL operation must surface before the desktop client's
        // receive timeout, rather than leaving an operator on "Saving...".
        sql.CommandTimeout(15);
        sql.EnableRetryOnFailure(3);
    });
});

// ---- Application services ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICurrentUser, CurrentUserService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ISequenceGenerator, SequenceGenerator>();
builder.Services.AddSingleton<ISecretProtector, SecretProtector>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<ILiveWeightBroadcaster, SignalRWeightBroadcaster>();
builder.Services.AddSingleton<WeighingService>();
builder.Services.AddHostedService<WeighingRecoveryService>();
builder.Services.AddScoped<UserStateService>();
builder.Services.AddScoped<LicenseService>();
builder.Services.AddScoped<BackupExecutionService>();
builder.Services.AddHostedService<BackupSchedulerService>();

// ---- Camera capture (Phase 6): vendor-abstracted providers + orchestration service ----
builder.Services.AddSingleton<ICameraCaptureProvider, IsapiCaptureProvider>();
builder.Services.AddSingleton<OnvifCaptureProvider>();
builder.Services.AddSingleton<ICameraCaptureProvider>(sp => sp.GetRequiredService<OnvifCaptureProvider>());
builder.Services.AddSingleton<RtspCaptureProvider>();
builder.Services.AddSingleton<ICameraCaptureProvider>(sp => sp.GetRequiredService<RtspCaptureProvider>());
builder.Services.AddSingleton<ICameraContinuousStreamProvider>(sp => sp.GetRequiredService<RtspCaptureProvider>());
builder.Services.AddSingleton<ICameraContinuousStreamProvider>(sp => sp.GetRequiredService<OnvifCaptureProvider>());
builder.Services.AddSingleton<ICameraCaptureProvider, SimulatorCaptureProvider>();
builder.Services.AddScoped<ICameraCaptureService, CameraCaptureService>();

// ---- Print engine (Phase 7): centralized A4/DotMatrix renderers + orchestration service ----
builder.Services.AddSingleton<IPrintRenderer, A4PdfRenderer>();
builder.Services.AddSingleton<IPrintRenderer, DotMatrixEscPRenderer>();
builder.Services.AddScoped<IPrintEngineService, PrintEngineService>();
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
QuestPDF.Drawing.FontManager.RegisterFontWithCustomName("CaneDevanagari", File.OpenRead(PrintFonts.PathFor("NotoSansDevanagari-Regular.ttf")));
QuestPDF.Drawing.FontManager.RegisterFontWithCustomName("CaneDevanagari", File.OpenRead(PrintFonts.PathFor("NotoSansDevanagari-Bold.ttf")));

// ---- SMS (Phase 10): generic HTTP provider, queue-only enqueue service, background delivery poller ----
builder.Services.AddHttpClient<ISmsProviderClient, GenericHttpSmsProvider>();
builder.Services.AddScoped<ISmsService, SmsService>();
builder.Services.AddHostedService<SmsQueueProcessor>();

// ---- Reports (Phase 11): generic PDF/Excel export shared by every report endpoint ----
builder.Services.AddSingleton<IReportExportService, ReportExportService>();
QuestPDF.Drawing.FontManager.RegisterFontWithCustomName("CaneLatin", File.OpenRead(PrintFonts.PathFor("Tinos-Regular.ttf")));
QuestPDF.Drawing.FontManager.RegisterFontWithCustomName("CaneLatin", File.OpenRead(PrintFonts.PathFor("Tinos-Bold.ttf")));

// ---- AuthN: JWT bearer, short-lived access tokens ----
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret (env CANE_JWT_SECRET) is not configured.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "CaneFactory",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "CaneFactoryClients",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(jwtSecret)))
        };
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                // allow SignalR websocket access_token query param
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

// ---- AuthZ: dynamic permission policies ----
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization();

// ---- Rate limiting & brute-force protection ----
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("auth", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

// ---- CORS allowlist ----
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000", "http://localhost:5000" };
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header
    });
    o.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ---- Security headers ----
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'";
    await next();
});
if (!app.Environment.IsDevelopment()) app.UseHsts();

app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<CaneFactory.API.Middleware.SecurityAuditMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();

// Server-side license gate. Public health/license status stay reachable so the client can show a useful error.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api")
        && !ctx.Request.Path.StartsWithSegments("/api/health")
        && !ctx.Request.Path.StartsWithSegments("/api/ping")
        && !ctx.Request.Path.StartsWithSegments("/api/license"))
    {
        var license = await ctx.RequestServices.GetRequiredService<LicenseService>().CheckAsync();
        if (!license.IsValid)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(license);
            return;
        }
    }
    await next();
});

// Global account-state gate: deactivated users and pending forced-password-change users
// are blocked on ALL business APIs (auth endpoints excluded so they can change the password).
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path;
    if (ctx.User.Identity?.IsAuthenticated == true
        && path.StartsWithSegments("/api")
        && !path.StartsWithSegments("/api/auth")
        && !path.StartsWithSegments("/api/ping")
        && !path.StartsWithSegments("/api/health"))
    {
        var sub = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(sub, out var uid))
        {
            var state = ctx.RequestServices.GetRequiredService<UserStateService>();
            if (!await state.IsActiveAsync(uid))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new
                { message = "Account is inactive or a password change is required before using the application." });
                return;
            }
        }
    }
    await next();
});

app.UseAuthorization();
app.MapControllers();
app.MapHub<WeightHub>("/hubs/weight");
app.MapGet("/api/ping", () => Results.Ok(new { status = "ok", service = "CaneFactory API", time = DateTime.UtcNow }));

// ---- Database create/migrate + seed ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) db.Database.EnsureCreated();
    else if (db.Database.GetMigrations().Any()) db.Database.Migrate();
    else db.Database.EnsureCreated();
    await DbSeeder.SeedAsync(db, app.Configuration);
}

app.Run();


