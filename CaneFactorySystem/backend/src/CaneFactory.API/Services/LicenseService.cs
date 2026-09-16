using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace CaneFactory.API.Services;

public sealed record LicenseStatus(bool IsValid, bool IsWarning, int DaysRemaining, string Message, DateTime? EndDate);

/// <summary>Single server-authoritative license policy used by login and every protected business route.</summary>
public sealed class LicenseService
{
    private readonly AppDbContext _db;
    public LicenseService(AppDbContext db) => _db = db;

    public async Task<LicenseStatus> CheckAsync()
    {
        var raw = await _db.SystemSettings.AsNoTracking().Where(s => s.Key == "LicenseEndDate").Select(s => s.Value).FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(raw)) return new(true, false, int.MaxValue, "License is not date-limited.", null);
        raw = raw.Trim();
        // The setting is stored as an ISO calendar date. Parsing it with the server's current
        // Windows culture made a valid value such as 2027-09-01 appear invalid on some hosts.
        if (!DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var end) &&
            !DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out end))
            return new(false, false, 0, "Software license configuration is invalid. Set LicenseEndDate as yyyy-MM-dd.", null);
        end = end.Date;
        var days = (end - DateTime.UtcNow.Date).Days;
        if (days < 0) return new(false, false, days, "Your software license has expired. Please renew/purchase a valid license to continue using the application.", end);
        if (days <= 7) return new(true, true, days, $"Your software license expires in {days} day(s). Please arrange renewal.", end);
        if (days <= 30) return new(true, true, days, $"Your software license expires in {days} days. Please arrange renewal.", end);
        return new(true, false, days, "License is valid.", end);
    }
}
