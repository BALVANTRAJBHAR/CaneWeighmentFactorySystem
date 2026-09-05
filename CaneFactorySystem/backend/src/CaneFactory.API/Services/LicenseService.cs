using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
        if (!DateTime.TryParse(raw, out var end)) return new(false, false, 0, "Software license configuration is invalid. Contact support.", null);
        end = end.Date;
        var days = (end - DateTime.UtcNow.Date).Days;
        if (days < 0) return new(false, false, days, "Your software license has expired. Please renew/purchase a valid license to continue using the application.", end);
        if (days <= 7) return new(true, true, days, $"Your software license expires in {days} day(s). Please arrange renewal.", end);
        if (days <= 30) return new(true, true, days, $"Your software license expires in {days} days. Please arrange renewal.", end);
        return new(true, false, days, "License is valid.", end);
    }
}
