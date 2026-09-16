using CaneFactory.Application.Interfaces;

namespace CaneFactory.API.Middleware;

/// <summary>Phase 13: audits every 401/403 API response (failed auth, missing permission, copied
/// URL, tampered ID) - not just successful business actions. Runs the request first, then inspects
/// the final status code, so it also catches 403s produced deep inside PermissionAuthorizationHandler
/// or an imperative Deny() check in a controller. Never breaks the actual response on audit failure.</summary>
public class SecurityAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityAuditMiddleware> _log;

    public SecurityAuditMiddleware(RequestDelegate next, ILogger<SecurityAuditMiddleware> log)
    {
        _next = next; _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        await _next(ctx);

        if (ctx.Response.StatusCode is 401 or 403 && ctx.Request.Path.StartsWithSegments("/api"))
        {
            try
            {
                var audit = ctx.RequestServices.GetRequiredService<IAuditService>();
                await audit.LogAsync(ctx.Response.StatusCode == 401 ? "Unauthenticated" : "PermissionDenied",
                    "Security", entity: "Request", entityId: ctx.Request.Path,
                    success: false, failureReason: $"{ctx.Request.Method} {ctx.Request.Path} -> {ctx.Response.StatusCode}");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SecurityAuditMiddleware failed to write audit log for {Path}", ctx.Request.Path);
            }
        }
    }
}
