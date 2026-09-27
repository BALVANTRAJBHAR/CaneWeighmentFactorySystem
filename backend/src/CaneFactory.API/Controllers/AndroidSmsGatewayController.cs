using CaneFactory.API.Services;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

/// <summary>Private Android SIM gateway protocol. Every call authenticates Device ID + API key;
/// JWT/user credentials are intentionally not accepted as device identity.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("android-gateway")]
[Route("api/android-sms-gateway")]
public sealed class AndroidSmsGatewayController : ControllerBase
{
    private const int MaxRetryCount = 3;
    private readonly AppDbContext _db;
    private readonly AndroidGatewayCredentialService _credentials;
    private readonly IHostEnvironment _environment;

    public AndroidSmsGatewayController(AppDbContext db, AndroidGatewayCredentialService credentials,
        IHostEnvironment environment)
    {
        _db = db;
        _credentials = credentials;
        _environment = environment;
    }

    [HttpPost("auth/validate")]
    public async Task<IActionResult> Validate(CancellationToken ct)
    {
        var auth = await AuthenticateAsync(requireActiveConfiguration: false, ct);
        if (auth.Error != null) return auth.Error;
        var config = auth.Config;
        return Ok(new
        {
            valid = true,
            active = config?.Enabled == true && config.ProviderType == "ANDROID_SIM",
            status = config?.Enabled == true ? "CONNECTED" : "DISABLED",
            simSlot = config?.AndroidSimSlot ?? auth.Device!.SimSlot,
            pollIntervalSeconds = config?.AndroidPollIntervalSeconds ?? auth.Device!.PollIntervalSeconds
        });
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat(CancellationToken ct)
    {
        var auth = await AuthenticateAsync(requireActiveConfiguration: false, ct);
        if (auth.Error != null) return auth.Error;
        var now = DateTime.UtcNow;
        auth.Device!.LastHeartbeatAt = now;
        auth.Device.LastSeenIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (auth.Config != null)
        {
            auth.Config.AndroidLastHeartbeatAt = now;
            auth.Config.AndroidConnectionStatus = auth.Config.Enabled ? "CONNECTED" : "DISABLED";
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new
        {
            active = auth.Config?.Enabled == true && auth.Config.ProviderType == "ANDROID_SIM",
            status = auth.Config?.Enabled == true ? "CONNECTED" : "DISABLED",
            serverTimeUtc = now
        });
    }

    [HttpGet("messages/pending")]
    public async Task<IActionResult> Pending([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var auth = await AuthenticateAsync(requireActiveConfiguration: true, ct);
        if (auth.Error != null) return auth.Error;
        var deviceId = auth.Device!.DeviceId;
        var now = DateTime.UtcNow;
        var staleBefore = now.AddMinutes(-2);

        var stale = await _db.SmsLogs.Where(x => x.ProviderType == "ANDROID_SIM" && x.DeviceId == deviceId &&
            x.Status == "PROCESSING" && x.PickedAt < staleBefore).ToListAsync(ct);
        foreach (var row in stale)
        {
            row.Status = row.AttemptCount >= MaxRetryCount ? "FAILED" : "RETRY_PENDING";
            row.FailureReason = "Gateway claim expired before delivery acknowledgement.";
            row.FailedAt = row.Status == "FAILED" ? now : null;
            row.NextAttemptAt = row.Status == "RETRY_PENDING" ? now : null;
        }
        if (stale.Count > 0) await _db.SaveChangesAsync(ct);

        var rows = await _db.SmsLogs.AsNoTracking()
            .Where(x => x.ProviderType == "ANDROID_SIM" && x.DeviceId == deviceId &&
                (x.Status == "QUEUED" || x.Status == "RETRY_PENDING") &&
                (x.NextAttemptAt == null || x.NextAttemptAt <= now) && x.AttemptCount < MaxRetryCount)
            .OrderBy(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 50))
            .Select(x => new
            {
                x.Id,
                mobileNumber = NormalizeIndianMobile(x.MobileNumber),
                message = x.MessageText,
                simSlot = x.SimSlot ?? auth.Config!.AndroidSimSlot,
                retryCount = x.AttemptCount,
                x.CreatedAt
            }).ToListAsync(ct);
        return Ok(new { items = rows });
    }

    [HttpPost("messages/{id:int}/processing")]
    public async Task<IActionResult> Processing(int id, CancellationToken ct)
    {
        var auth = await AuthenticateAsync(requireActiveConfiguration: true, ct);
        if (auth.Error != null) return auth.Error;
        var now = DateTime.UtcNow;
        var count = await _db.SmsLogs.Where(x => x.Id == id && x.ProviderType == "ANDROID_SIM" &&
                x.DeviceId == auth.Device!.DeviceId && (x.Status == "QUEUED" || x.Status == "RETRY_PENDING") &&
                x.AttemptCount < MaxRetryCount)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, "PROCESSING")
                .SetProperty(x => x.PickedAt, now)
                .SetProperty(x => x.NextAttemptAt, (DateTime?)null)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct);
        return count == 1
            ? Ok(new { claimed = true })
            : Conflict(new { message = "Message was already claimed, completed, or is unavailable." });
    }

    [HttpPost("messages/{id:int}/sent")]
    public async Task<IActionResult> Sent(int id, [FromBody] GatewaySentRequest req, CancellationToken ct)
    {
        var auth = await AuthenticateAsync(requireActiveConfiguration: false, ct);
        if (auth.Error != null) return auth.Error;
        var row = await _db.SmsLogs.FirstOrDefaultAsync(x => x.Id == id && x.ProviderType == "ANDROID_SIM" &&
            x.DeviceId == auth.Device!.DeviceId, ct);
        if (row == null) return NotFound(new { message = "SMS queue item was not found." });
        if (row.Status == "SENT") return Ok(new { sent = true, duplicateAcknowledgement = true });
        if (row.Status is not ("PROCESSING" or "RETRY_PENDING" or "FAILED"))
            return Conflict(new { message = "Only a claimed/recovering message can be marked sent." });
        row.Status = "SENT";
        row.SentAt = DateTime.UtcNow;
        row.FailedAt = null;
        row.FailureReason = null;
        row.ProviderMessageId = Truncate(req.ProviderMessageId, 200);
        await _db.SaveChangesAsync(ct);
        return Ok(new { sent = true });
    }

    [HttpPost("messages/{id:int}/failed")]
    public async Task<IActionResult> Failed(int id, [FromBody] GatewayFailedRequest req, CancellationToken ct)
    {
        var auth = await AuthenticateAsync(requireActiveConfiguration: false, ct);
        if (auth.Error != null) return auth.Error;
        var row = await _db.SmsLogs.FirstOrDefaultAsync(x => x.Id == id && x.ProviderType == "ANDROID_SIM" &&
            x.DeviceId == auth.Device!.DeviceId, ct);
        if (row == null) return NotFound(new { message = "SMS queue item was not found." });
        if (row.Status == "SENT") return Conflict(new { message = "A sent message cannot be failed or retried." });
        if (row.Status != "PROCESSING") return Conflict(new { message = "Only a claimed message can report failure." });
        row.FailureReason = Truncate(string.IsNullOrWhiteSpace(req.FailureReason) ? "Android SmsManager reported failure." : req.FailureReason, 500);
        row.FailedAt = DateTime.UtcNow;
        if (row.AttemptCount >= MaxRetryCount)
        {
            row.Status = "FAILED";
            row.NextAttemptAt = null;
        }
        else
        {
            row.Status = "RETRY_PENDING";
            row.NextAttemptAt = DateTime.UtcNow.AddSeconds(Math.Min(120, 10 * row.AttemptCount));
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { status = row.Status, retryCount = row.AttemptCount });
    }

    private async Task<GatewayAuthentication> AuthenticateAsync(bool requireActiveConfiguration, CancellationToken ct)
    {
        if (!Request.IsHttps && !_environment.IsDevelopment())
            return GatewayAuthentication.Fail(StatusCode(StatusCodes.Status426UpgradeRequired,
                new { message = "Android SIM Gateway requires HTTPS." }));
        var deviceId = Request.Headers["X-Device-Id"].FirstOrDefault()?.Trim();
        var apiKey = Request.Headers["X-Device-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(apiKey))
            return GatewayAuthentication.Fail(Unauthorized(new { message = "Device credentials are required." }));
        var device = await _db.AndroidSmsGatewayDevices.FirstOrDefaultAsync(d =>
            d.DeviceId == deviceId && d.Status && !d.IsDeleted, ct);
        if (device == null || !_credentials.Verify(apiKey, device.ApiKeyHash, device.ApiKeySalt))
            return GatewayAuthentication.Fail(Unauthorized(new { message = "Android SIM Gateway credentials are invalid." }));
        var config = await _db.SmsConfigs.FirstOrDefaultAsync(s => !s.IsDeleted &&
            s.ProviderType == "ANDROID_SIM" && s.AndroidDeviceId == deviceId, ct);
        if (requireActiveConfiguration && (config == null || !config.Enabled || !config.AndroidCredentialVerified))
            return GatewayAuthentication.Fail(StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Android SIM SMS configuration is disabled or not active." }));
        return new GatewayAuthentication(device, config, null);
    }

    private static string NormalizeIndianMobile(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 10) return "+91" + digits;
        if (digits.Length == 12 && digits.StartsWith("91")) return "+" + digits;
        return value;
    }

    private static string? Truncate(string? value, int length) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim()[..Math.Min(value.Trim().Length, length)];

    private sealed record GatewayAuthentication(
        CaneFactory.Domain.Entities.AndroidSmsGatewayDevice? Device,
        CaneFactory.Domain.Entities.SmsConfig? Config,
        IActionResult? Error)
    {
        public static GatewayAuthentication Fail(IActionResult error) => new(null, null, error);
    }
}

public sealed class GatewaySentRequest
{
    public string? ProviderMessageId { get; set; }
}

public sealed class GatewayFailedRequest
{
    public string? FailureReason { get; set; }
}
