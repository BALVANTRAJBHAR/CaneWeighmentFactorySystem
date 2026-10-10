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
using Microsoft.AspNetCore.HttpOverrides;
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
var connString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connString))
    throw new InvalidOperationException(
        "ConnectionStrings:Default is empty. Restore the server-only appsettings.Production.json/appsettings.json or set CANE_CONNECTION_STRING before starting IIS.");
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
builder.Services.AddSingleton<AndroidGatewayCredentialService>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<ILiveWeightBroadcaster, SignalRWeightBroadcaster>();
builder.Services.AddSingleton<WeighingService>();
builder.Services.AddHostedService<WeighingRecoveryService>();
builder.Services.AddScoped<UserStateService>();
builder.Services.AddScoped<FarmerAccountService>();
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
builder.Services.AddScoped<CaneFactory.API.Services.RateOverrideService>();

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
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new InvalidOperationException(
        "Jwt:Secret is empty. Restore the server-only configuration or set CANE_JWT_SECRET before starting IIS.");
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

// Tailscale Serve/Funnel terminates TLS on this machine, then proxies to the
// API over loopback HTTP. Trust forwarded scheme/IP headers only from that
// local proxy so Request.IsHttps remains reliable for the Android SMS gateway.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
    o.KnownProxies.Add(System.Net.IPAddress.Loopback);
    o.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
});

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
    o.AddPolicy("android-gateway", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Request.Headers["X-Device-Id"].FirstOrDefault()
                ?? ctx.Connection.RemoteIpAddress?.ToString()
                ?? "android-gateway",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
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

// Must run before HTTPS checks, rate limiting, authentication and logging.
app.UseForwardedHeaders();

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
// are blocked on all protected APIs. Only unauthenticated login/refresh/recovery endpoints are
// excluded; the forced change-password endpoint checks account/token state but permits the
// MustChangePassword flag by design.
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path;
    var isPublicAuthEndpoint = path.StartsWithSegments("/api/auth/login")
        || path.StartsWithSegments("/api/auth/refresh")
        || path.StartsWithSegments("/api/auth/forgot-password");
    if (ctx.User.Identity?.IsAuthenticated == true
        && path.StartsWithSegments("/api")
        && !isPublicAuthEndpoint
        && !path.StartsWithSegments("/api/ping")
        && !path.StartsWithSegments("/api/health"))
    {
        var sub = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(sub, out var uid))
        {
            var state = ctx.RequestServices.GetRequiredService<UserStateService>();
            var versionText = ctx.User.FindFirst("ver")?.Value;
            var requirePasswordChanged = !path.StartsWithSegments("/api/auth/change-password");
            if (!int.TryParse(versionText, out var tokenVersion)
                || !await state.IsActiveAsync(uid, tokenVersion, requirePasswordChanged))
            {
                // 401 lets supported clients rotate a legacy/stale token once. If its refresh
                // session was revoked (password/status change), refresh fails and the client logs out.
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new
                { message = "Session is no longer valid, or a password change is required. Please login again." });
                return;
            }
        }
    }
    await next();
});

// Farmer identities are deliberately confined to the read-only self-service API.  Permission
// claims are not trusted as a record-ownership boundary, including claims in older tokens.
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true && ctx.User.IsInRole("Farmer"))
    {
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/hubs"))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new
            { message = "Farmer accounts cannot access live factory device streams." });
            return;
        }

        if (!path.StartsWithSegments("/api"))
        {
            await next();
            return;
        }

        var allowed = path.StartsWithSegments("/api/farmer")
            || path.StartsWithSegments("/api/auth")
            || path.StartsWithSegments("/api/dashboard/header")
            || path.StartsWithSegments("/api/health")
            || path.StartsWithSegments("/api/ping")
            || path.StartsWithSegments("/api/license");
        if (!allowed)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new
            { message = "Farmer accounts have read-only access to their own records only." });
            return;
        }

        var remote = ctx.Connection.RemoteIpAddress;
        if (!ctx.Request.IsHttps && (remote == null || !System.Net.IPAddress.IsLoopback(remote)))
        {
            ctx.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await ctx.Response.WriteAsJsonAsync(new { message = "Farmer access requires HTTPS." });
            return;
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
    await scope.ServiceProvider.GetRequiredService<FarmerAccountService>()
        .SynchronizeExistingGrowersAsync();
}

app.Run();
