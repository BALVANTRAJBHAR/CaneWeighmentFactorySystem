using System.Text.Json;
using CaneFactory.API.Services;

namespace CaneFactory.API.Middleware;

/// <summary>Global exception handler: no stack traces, connection strings or secrets ever reach clients.</summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _log;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            BackupScheduleRuntime.EnsureStarted(
                ctx.RequestServices.GetRequiredService<IServiceScopeFactory>(),
                ctx.RequestServices.GetRequiredService<ILoggerFactory>());
            await _next(ctx);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled exception on {Path}", ctx.Request.Path);
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    message = "An unexpected error occurred. Please try again or contact the administrator.",
                    reference = ctx.TraceIdentifier
                }));
            }
        }
    }
}
