using CaneFactory.Application.Common;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CaneFactory.Infrastructure.Sms;

/// <summary>Background poller that turns QUEUED/RETRY_PENDING SmsLog rows into real HTTP sends - the
/// only place in the system that ever makes a network call for SMS, fully decoupled from the request
/// thread that queued them. Controlled retry with exponential backoff (3, 9, 27, 81 minutes), FAILED
/// permanently after 5 attempts. Runs every 15 seconds.</summary>
public class SmsQueueProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmsQueueProcessor> _log;

    public SmsQueueProcessor(IServiceScopeFactory scopeFactory, ILogger<SmsQueueProcessor> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (Exception ex) { _log.LogWarning(ex, "SMS queue processing cycle failed."); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (TaskCanceledException) { /* shutting down */ }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<ISmsProviderClient>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var cfg = await db.SmsConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted, ct);
        if (cfg == null || !cfg.Enabled) return;

        var now = DateTime.UtcNow;
        var batch = await db.SmsLogs
            .Where(l => (l.Status == "QUEUED" || l.Status == "RETRY_PENDING") && (l.NextAttemptAt == null || l.NextAttemptAt <= now))
            .OrderBy(l => l.CreatedAt).Take(20).ToListAsync(ct);
        if (batch.Count == 0) return;

        var apiKey = cfg.ApiKeyEncrypted != null ? protector.Unprotect(cfg.ApiKeyEncrypted) : null;
        var apiSecret = cfg.ApiSecretEncrypted != null ? protector.Unprotect(cfg.ApiSecretEncrypted) : null;

        foreach (var log in batch)
        {
            log.Status = "PROCESSING";
            log.AttemptCount++;
            await db.SaveChangesAsync(ct);

            var (success, _, error) = await provider.SendAsync(cfg, apiKey, apiSecret, log.MobileNumber, log.MessageText, ct);
            if (success)
            {
                log.Status = "SENT";
                log.SentAt = DateTime.UtcNow;
                log.FailureReason = null;
            }
            else if (log.AttemptCount >= 5)
            {
                log.Status = "FAILED";
                log.FailureReason = Truncate(error);
            }
            else
            {
                log.Status = "RETRY_PENDING";
                log.FailureReason = Truncate(error);
                log.NextAttemptAt = DateTime.UtcNow.AddMinutes(Math.Pow(3, log.AttemptCount));
            }
            await db.SaveChangesAsync(ct);

            await audit.LogAsync(success ? "SmsSent" : "SmsFailed", "Sms", "SmsLog", log.Id.ToString(),
                newValue: new { log.EventCode, log.ReferenceId, MaskedMobile = SmsMask.Number(log.MobileNumber), log.Status, log.AttemptCount },
                success: success, failureReason: success ? null : error);
        }
    }

    private static string? Truncate(string? s) => s == null ? null : (s.Length <= 500 ? s : s[..500]);
}
