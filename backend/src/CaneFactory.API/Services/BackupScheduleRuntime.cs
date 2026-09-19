using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Services;

/// <summary>
/// In-process SQL Express scheduler. Successful slots are persisted in SystemSettings so an IIS
/// recycle cannot create duplicate backups, while missed due slots are recovered after restart.
/// Failed slots are retried every two minutes instead of being incorrectly marked complete.
/// </summary>
public static class BackupScheduleRuntime
{
    private static int _started;
    private static readonly object Sync = new();
    private static readonly HashSet<string> CompletedSlots = new();
    private static readonly Dictionary<string, DateTime> RetryAfterUtc = new();

    public static void EnsureStarted(IServiceScopeFactory scopes, ILoggerFactory loggerFactory)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _ = Task.Run(() => RunAsync(scopes, loggerFactory));
    }

    private static async Task RunAsync(IServiceScopeFactory scopes, ILoggerFactory loggerFactory)
    {
        var log = loggerFactory.CreateLogger("CaneFactory.BackupScheduler");
        while (true)
        {
            try
            {
                await RunDueBackupsAsync(scopes, loggerFactory, log);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Backup scheduler check failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(20));
        }
    }

    private static async Task RunDueBackupsAsync(
        IServiceScopeFactory scopes, ILoggerFactory loggerFactory, ILogger log)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = await db.BackupConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted);
        if (config?.Enabled != true || !TimeOnly.TryParse(config.TimeOfDay, out var anchor))
            return;

        var now = DateTime.Now;
        var policyActiveFrom = (config.UpdatedAt ?? config.CreatedAt).ToLocalTime();

        var fullDue = LatestFullDue(config.Frequency, anchor, now);
        if (fullDue >= policyActiveFrom)
        {
            await RunSlotIfNeededAsync(db, loggerFactory, log, config,
                BackupKind.Full, fullDue);
        }

        var fullSuccessExists = await db.SystemSettings.AsNoTracking()
            .AnyAsync(x => x.Key == SuccessKey(BackupKind.Full));
        if (!fullSuccessExists) return;

        if (config.DifferentialEnabled)
        {
            var differentialDue = LatestIntervalDue(anchor, now,
                TimeSpan.FromHours(Math.Max(1, config.DifferentialIntervalHours)));
            if (differentialDue >= policyActiveFrom)
            {
                await RunSlotIfNeededAsync(db, loggerFactory, log, config,
                    BackupKind.Differential, differentialDue);
            }
        }

        if (config.TransactionLogEnabled)
        {
            var transactionLogDue = LatestIntervalDue(anchor, now,
                TimeSpan.FromMinutes(Math.Max(5, config.TransactionLogIntervalMinutes)));
            if (transactionLogDue >= policyActiveFrom)
            {
                await RunSlotIfNeededAsync(db, loggerFactory, log, config,
                    BackupKind.TransactionLog, transactionLogDue);
            }
        }
    }

    private static DateTime LatestFullDue(
        string frequency, TimeOnly anchor, DateTime now)
    {
        var todayAtAnchor = now.Date.Add(anchor.ToTimeSpan());
        if (frequency.Equals("Hourly", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = now.Date.AddHours(now.Hour).AddMinutes(anchor.Minute);
            return candidate > now ? candidate.AddHours(-1) : candidate;
        }

        if (frequency.Equals("Weekly", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = todayAtAnchor.AddDays(-(int)now.DayOfWeek);
            return candidate > now ? candidate.AddDays(-7) : candidate;
        }

        return todayAtAnchor > now ? todayAtAnchor.AddDays(-1) : todayAtAnchor;
    }

    private static DateTime LatestIntervalDue(
        TimeOnly anchor, DateTime now, TimeSpan interval)
    {
        var start = now.Date.Add(anchor.ToTimeSpan());
        if (start > now) start = start.AddDays(-1);
        var periods = (now.Ticks - start.Ticks) / interval.Ticks;
        return start.AddTicks(periods * interval.Ticks);
    }

    private static async Task RunSlotIfNeededAsync(
        AppDbContext db, ILoggerFactory loggerFactory, ILogger log,
        BackupConfig config, BackupKind kind, DateTime dueAt)
    {
        var slot = $"{kind}:{dueAt:yyyyMMddHHmm}";
        var successKey = SuccessKey(kind);
        var persistedSlot = await db.SystemSettings.AsNoTracking()
            .Where(x => x.Key == successKey)
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        if (string.Equals(persistedSlot, slot, StringComparison.Ordinal)) return;

        lock (Sync)
        {
            if (CompletedSlots.Contains(slot)) return;
            if (RetryAfterUtc.TryGetValue(slot, out var retryAt)
                && retryAt > DateTime.UtcNow)
                return;
            RetryAfterUtc[slot] = DateTime.UtcNow.AddMinutes(2);
        }

        var executor = new BackupExecutionService(
            db, loggerFactory.CreateLogger<BackupExecutionService>());
        var result = await executor.CreateAsync(config, kind, copyOnly: false);
        await SaveRunStatusAsync(db, kind, slot, result);

        if (!result.Success)
        {
            log.LogError(
                "Scheduled {Kind} backup for slot {Slot} failed and will retry: {Message}",
                kind, slot, result.Message);
            return;
        }

        lock (Sync)
        {
            CompletedSlots.Add(slot);
            RetryAfterUtc.Remove(slot);
            if (CompletedSlots.Count > 1000) CompletedSlots.Clear();
        }
        log.LogInformation("Scheduled {Kind} backup for slot {Slot}: {Message}",
            kind, slot, result.Message);
    }

    private static string SuccessKey(BackupKind kind) =>
        $"Backup.LastSuccessfulSlot.{kind}";

    private static async Task SaveRunStatusAsync(
        AppDbContext db, BackupKind kind, string slot, BackupOperationResult result)
    {
        await SetSettingAsync(db, "Backup.LastAttemptUtc",
            DateTime.UtcNow.ToString("O"));
        await SetSettingAsync(db, "Backup.LastKind", kind.ToString());
        await SetSettingAsync(db, "Backup.LastResult",
            result.Success ? "SUCCESS" : "FAILED");
        await SetSettingAsync(db, "Backup.LastMessage", result.Message);
        await SetSettingAsync(db, "Backup.LastFile",
            result.FilePath ?? string.Empty);
        if (result.Success)
            await SetSettingAsync(db, SuccessKey(kind), slot);
        await db.SaveChangesAsync();
    }

    private static async Task SetSettingAsync(
        AppDbContext db, string key, string value)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key);
        if (setting == null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
    }
}
