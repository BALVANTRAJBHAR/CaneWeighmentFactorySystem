using System.Security.Cryptography;
using System.Net;
using System.Text;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Weighing;
using CaneFactory.Domain.Entities;
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

    [HttpGet("mode")]
    public async Task<IActionResult> Mode()
    {
        var expected = Environment.GetEnvironmentVariable("CANE_SCALE_AGENT_KEY")
            ?? Environment.GetEnvironmentVariable("CANE_SCALE_AGENT_KEY", EnvironmentVariableTarget.Machine)
            ?? _configuration["ScaleBridge:AgentKey"];
        var supplied = Request.Headers["X-Cane-Scale-Key"].ToString();
        if (string.IsNullOrWhiteSpace(expected)) return StatusCode(503, new { message = "Scale Bridge is not configured." });
        if (!FixedTimeEquals(expected, supplied)) return Unauthorized(new { message = "Invalid Scale Bridge key." });
        var localOnly = await _db.WeightRules.AsNoTracking().Where(x => !x.IsDeleted)
            .Select(x => x.LiveWeightOnlyOnDigitizerPc).FirstOrDefaultAsync();
        return Ok(new { serverStreamingEnabled = !localOnly, liveWeightOnlyOnDigitizerPc = localOnly });
    }

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

        var localOnly = await _db.WeightRules.AsNoTracking().Where(x => !x.IsDeleted)
            .Select(x => x.LiveWeightOnlyOnDigitizerPc).FirstOrDefaultAsync();
        if (localOnly)
            return Ok(new { message = "Server streaming is disabled; use the Scale Bridge loopback reading on the digitizer PC.", accepted = false });

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

        // Persist the actual LAN host only when it changes.  This is used by
        // the optional developer safety rule that permits saving only from the
        // PC physically attached to the digitizer/Scale Bridge.
        var sourceIp = Request.HttpContext.Connection.RemoteIpAddress;
        var normalizedIp = sourceIp?.IsIPv4MappedToIPv6 == true
            ? sourceIp.MapToIPv4().ToString()
            : sourceIp?.ToString();
        if (!string.IsNullOrWhiteSpace(normalizedIp))
        {
            var hostSetting = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == "ScaleBridgeHostIp");
            if (hostSetting == null)
            {
                _db.SystemSettings.Add(new SystemSetting { Key = "ScaleBridgeHostIp", Value = normalizedIp, Description = "Last LAN host accepted by Scale Bridge." });
                await _db.SaveChangesAsync();
            }
            else if (!string.Equals(hostSetting.Value, normalizedIp, StringComparison.OrdinalIgnoreCase))
            {
                hostSetting.Value = normalizedIp;
                hostSetting.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }

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
