using System.Security.Cryptography;
using System.Text;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Weighing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

/// <summary>LAN-only ingress for the companion process running on the PC that physically owns the
/// USB-to-serial converter. It is deliberately separate from user JWT authentication because it
/// runs unattended, and is protected by a server-side secret header.</summary>
[ApiController]
[Route("api/scale-bridge")]
public sealed class ScaleBridgeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly WeighingService _weighing;
    private readonly IConfiguration _configuration;

    public ScaleBridgeController(AppDbContext db, WeighingService weighing, IConfiguration configuration)
        => (_db, _weighing, _configuration) = (db, weighing, configuration);

    [HttpPost("reading")]
    public async Task<IActionResult> Reading([FromBody] ScaleBridgeReading request)
    {
        // Environment variable is preferred. The configuration fallback makes IIS deployment
        // deterministic when a machine variable was created after the IIS worker had started.
        var expected = Environment.GetEnvironmentVariable("CANE_SCALE_AGENT_KEY")
            ?? _configuration["ScaleBridge:AgentKey"];
        var supplied = Request.Headers["X-Cane-Scale-Key"].ToString();
        if (string.IsNullOrWhiteSpace(expected))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Scale Bridge is not enabled on this server. Set CANE_SCALE_AGENT_KEY or ScaleBridge:AgentKey, then restart the API." });
        if (!FixedTimeEquals(expected, supplied)) return Unauthorized(new { message = "Invalid Scale Bridge key." });

        var device = await _db.WeighingDevices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DeviceId);
        if (device == null)
            return Conflict(new { message = $"No weighing device exists with DeviceId {request.DeviceId}." });
        if (device.IsDeleted || !device.ActiveConfiguration || !device.IsEnabled)
            return Conflict(new
            {
                message = $"DeviceId {request.DeviceId} is not eligible for remote readings.",
                device.IsDeleted,
                device.ActiveConfiguration,
                device.IsEnabled
            });
        if (!string.Equals(device.ConnectionType, "RemoteAgent", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = "Device is not configured for Remote Agent mode." });
        if (request.WeightKg is < -1000000 or > 1000000)
            return BadRequest(new { message = "Weight is outside the accepted range." });

        _weighing.ReceiveRemoteReading(device.DeviceName, request.WeightKg, request.Stable,
            string.IsNullOrWhiteSpace(request.ReaderState) ? "READING" : request.ReaderState, request.Error);
        return Ok(new { message = "Reading accepted." });
    }

    private static bool FixedTimeEquals(string expected, string supplied)
    {
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(supplied);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}

public sealed class ScaleBridgeReading
{
    public int DeviceId { get; set; }
    public decimal WeightKg { get; set; }
    public bool Stable { get; set; }
    public string ReaderState { get; set; } = "READING";
    public string? Error { get; set; }
}
