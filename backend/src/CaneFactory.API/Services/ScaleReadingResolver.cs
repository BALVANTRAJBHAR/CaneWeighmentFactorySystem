using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CaneFactory.Application.DTOs;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Weighing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CaneFactory.API.Services;

public sealed record ResolvedScaleReading(decimal WeightKg, bool LocalOnly, string? SessionId, long ZeroCounter);

/// <summary>Chooses the authoritative scale reading. In normal mode it uses the server's live
/// reader. In local-only mode it validates a short-lived HMAC proof minted only by the loopback
/// Scale Bridge on the digitizer PC, so a browser/mobile/other PC cannot invent a weight.</summary>
public sealed class ScaleReadingResolver
{
    private readonly AppDbContext _db;
    private readonly WeighingService _weighing;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;

    public ScaleReadingResolver(AppDbContext db, WeighingService weighing, IConfiguration configuration, IMemoryCache cache) =>
        (_db, _weighing, _configuration, _cache) = (db, weighing, configuration, cache);

    public async Task<bool> IsLocalOnlyAsync() => await _db.WeightRules.AsNoTracking()
        .Where(r => !r.IsDeleted).Select(r => r.LiveWeightOnlyOnDigitizerPc).FirstOrDefaultAsync();

    public async Task<(ResolvedScaleReading? reading, string? error)> ResolveAsync(IScaleReadingProofRequest request)
    {
        if (!await IsLocalOnlyAsync())
        {
            return _weighing.TryGetUsableWeight(out var liveKg, out var liveError)
                ? (new ResolvedScaleReading(liveKg, false, null, 0), null)
                : (null, liveError);
        }

        var key = Environment.GetEnvironmentVariable("CANE_SCALE_AGENT_KEY")
            ?? Environment.GetEnvironmentVariable("CANE_SCALE_AGENT_KEY", EnvironmentVariableTarget.Machine)
            ?? _configuration["ScaleBridge:AgentKey"];
        if (string.IsNullOrWhiteSpace(key))
            return (null, "Scale Bridge proof validation is not configured on the server.");
        if (!request.ScaleReadingStable)
            return (null, "Wait until the local digitizer reading is stable before saving.");
        if (request.ScaleReadingCapturedAtUtc is not DateTime captured)
            return (null, "The local digitizer did not provide a reading timestamp.");

        captured = captured.Kind == DateTimeKind.Utc ? captured : captured.ToUniversalTime();
        if (Math.Abs((DateTime.UtcNow - captured).TotalSeconds) > 15)
            return (null, "The local digitizer reading is stale. Wait for a fresh reading and save again.");
        var session = request.ScaleBridgeSessionId?.Trim() ?? string.Empty;
        var nonce = request.ScaleReadingNonce?.Trim() ?? string.Empty;
        var supplied = request.ScaleReadingSignature?.Trim() ?? string.Empty;
        if (!Guid.TryParse(session, out _) || nonce.Length is < 8 or > 100 || supplied.Length < 32)
            return (null, "The local digitizer reading proof is missing or invalid.");
        if (request.ScaleReadingKg is < -1000000m or > 1000000m || request.ScaleBridgeZeroCounter < 0)
            return (null, "The local digitizer reading is outside the accepted range.");

        var unixMs = new DateTimeOffset(captured).ToUnixTimeMilliseconds();
        var canonical = string.Join('|', request.ScaleReadingKg.ToString("F2", CultureInfo.InvariantCulture),
            request.ScaleReadingStable ? "1" : "0", unixMs.ToString(CultureInfo.InvariantCulture), session,
            request.ScaleBridgeZeroCounter.ToString(CultureInfo.InvariantCulture), nonce);
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(canonical)));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(supplied.ToUpperInvariant())))
            return (null, "The local digitizer reading proof was rejected.");
        if (_cache.TryGetValue($"scale-proof:{supplied}", out _))
            return (null, "This digitizer reading was already used. Wait for the current reading and save again.");
        _cache.Set($"scale-proof:{supplied}", true, TimeSpan.FromMinutes(2));
        return (new ResolvedScaleReading(request.ScaleReadingKg, true, session, request.ScaleBridgeZeroCounter), null);
    }

    public async Task<bool> TryReleasePlatformLockAsync(ResolvedScaleReading reading)
    {
        if (!reading.LocalOnly || string.IsNullOrWhiteSpace(reading.SessionId)) return false;
        var platform = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == "WeighbridgePlatformClearRequired");
        if (platform?.Value != "1") return true;
        var lastSession = await _db.SystemSettings.AsNoTracking().Where(x => x.Key == "ScaleBridgeLastSavedSession")
            .Select(x => x.Value).FirstOrDefaultAsync();
        var lastCounterText = await _db.SystemSettings.AsNoTracking().Where(x => x.Key == "ScaleBridgeLastSavedZeroCounter")
            .Select(x => x.Value).FirstOrDefaultAsync();
        _ = long.TryParse(lastCounterText, out var lastCounter);
        var cleared = string.Equals(lastSession, reading.SessionId, StringComparison.OrdinalIgnoreCase)
            ? reading.ZeroCounter > lastCounter
            : reading.ZeroCounter > 0;
        if (cleared)
        {
            platform.Value = "0";
            platform.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return cleared;
    }

    public async Task RecordSavedAsync(ResolvedScaleReading reading)
    {
        if (!reading.LocalOnly || string.IsNullOrWhiteSpace(reading.SessionId)) return;
        await UpsertAsync("ScaleBridgeLastSavedSession", reading.SessionId);
        await UpsertAsync("ScaleBridgeLastSavedZeroCounter", reading.ZeroCounter.ToString(CultureInfo.InvariantCulture));
    }

    private async Task UpsertAsync(string key, string value)
    {
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key);
        if (setting == null) _db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        else { setting.Value = value; setting.UpdatedAt = DateTime.UtcNow; }
    }
}
