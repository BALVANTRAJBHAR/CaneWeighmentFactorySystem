using CaneFactory.API.Auth;
using CaneFactory.API.Services;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security;

namespace CaneFactory.API.Controllers;

public class BackupConfigDto
{
    public bool Enabled { get; set; }
    [Required] public string Frequency { get; set; } = "Daily";
    [Required] public string TimeOfDay { get; set; } = "02:00";
    public int RetentionDays { get; set; } = 14;
    [Required] public string BackupFolderPath { get; set; } = "";
    public bool DifferentialEnabled { get; set; }
    public int DifferentialIntervalHours { get; set; } = 4;
    public bool TransactionLogEnabled { get; set; }
    public int TransactionLogIntervalMinutes { get; set; } = 30;
}

/// <summary>SQL Server Express-safe backup policy and direct current-backup download.</summary>
[ApiController]
[Authorize]
[Route("api/backup")]
public class BackupController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    private readonly IConfiguration _config;
    private readonly BackupExecutionService _executor;

    public BackupController(AppDbContext db, IAuditService audit, ICurrentUser current,
        IConfiguration config, ILogger<BackupController> logger)
        => (_db, _audit, _current, _config, _executor) = (db, audit, current, config, new BackupExecutionService(db, logger));

    [HasPermission("Backup.View")]
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig() => Ok(await _db.BackupConfigs.FirstOrDefaultAsync(b => !b.IsDeleted));

    [HasPermission("Backup.View")]
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var cfg = await _db.BackupConfigs.AsNoTracking()
            .FirstOrDefaultAsync(b => !b.IsDeleted) ?? new BackupConfig();
        var values = await _db.SystemSettings.AsNoTracking()
            .Where(x => x.Key.StartsWith("Backup.Last"))
            .ToDictionaryAsync(x => x.Key, x => x.Value);
        string? newestFile = null;
        try
        {
            if (Directory.Exists(cfg.BackupFolderPath))
                newestFile = Directory.EnumerateFiles(cfg.BackupFolderPath, "*.bak")
                    .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
                    .Select(Path.GetFileName)
                    .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            newestFile = $"Folder cannot be read by API/IIS: {ex.Message}";
        }

        return Ok(new
        {
            enabled = cfg.Enabled,
            serverTime = DateTime.Now,
            nextFullBackupAt = NextFullRun(cfg.Frequency, cfg.TimeOfDay),
            lastAttemptUtc = values.GetValueOrDefault("Backup.LastAttemptUtc"),
            lastKind = values.GetValueOrDefault("Backup.LastKind"),
            lastResult = values.GetValueOrDefault("Backup.LastResult"),
            lastMessage = values.GetValueOrDefault("Backup.LastMessage"),
            lastFile = values.GetValueOrDefault("Backup.LastFile"),
            newestConfiguredFolderBackup = newestFile
        });
    }

    [HasPermission("Backup.Configure")]
    [HttpPut("config")]
    public async Task<IActionResult> UpdateConfig([FromBody] BackupConfigDto src)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!(new[] { "Daily", "Hourly", "Weekly" }).Contains(src.Frequency))
            return BadRequest(new { message = "Frequency must be Daily, Hourly, or Weekly." });
        if (!TimeOnly.TryParseExact(src.TimeOfDay, "HH:mm", out _))
            return BadRequest(new { message = "Time must be in 24-hour HH:mm format. Example: 01:00" });
        if (src.RetentionDays < 1) return BadRequest(new { message = "Retention Days must be at least 1." });

        string folder;
        try
        {
            if (string.IsNullOrWhiteSpace(src.BackupFolderPath) || !Path.IsPathFullyQualified(src.BackupFolderPath))
                return BadRequest(new { message = "Backup Folder Path must be a full local or UNC path. Example: D:\\CaneFactoryBackup" });
            folder = Path.GetFullPath(src.BackupFolderPath.Trim());
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return BadRequest(new { message = $"Backup folder is not available to the API service: {ex.Message}" });
        }

        var b = await _db.BackupConfigs.FirstOrDefaultAsync(x => !x.IsDeleted);
        var isNew = b == null;
        b ??= new BackupConfig { CreatedBy = _current.UserId };
        var old = new { b.Enabled, b.Frequency, b.TimeOfDay, b.RetentionDays, b.BackupFolderPath };
        b.Enabled = src.Enabled; b.Frequency = src.Frequency; b.TimeOfDay = src.TimeOfDay;
        b.RetentionDays = src.RetentionDays; b.BackupFolderPath = folder.TrimEnd('\\', '/');
        b.DifferentialEnabled = src.DifferentialEnabled;
        b.DifferentialIntervalHours = Math.Clamp(src.DifferentialIntervalHours, 1, 24);
        b.TransactionLogEnabled = src.TransactionLogEnabled;
        b.TransactionLogIntervalMinutes = Math.Clamp(src.TransactionLogIntervalMinutes, 5, 1440);
        if (isNew) _db.BackupConfigs.Add(b);
        else { b.UpdatedAt = DateTime.UtcNow; b.UpdatedBy = _current.UserId; }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SystemSettingChange", "Backup", "BackupConfig", b.Id.ToString(), oldValue: old, newValue: src);
        return Ok(new { message = $"Backup policy saved: {b.Frequency} FULL backup starts at {b.TimeOfDay}; retention is {b.RetentionDays} day(s)." });
    }

    [HasPermission("Backup.Configure")]
    [HttpPost("current/download")]
    public async Task<IActionResult> DownloadCurrentBackup(CancellationToken cancellationToken)
    {
        var result = await _executor.CreateAsync(BackupKind.Full, copyOnly: true, cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.FilePath))
            return Conflict(new { message = result.Message });
        await _audit.LogAsync("BackupDownloaded", "Backup", "BackupConfig", null,
            newValue: new { fileName = Path.GetFileName(result.FilePath), type = "COPY_ONLY_FULL" });
        return PhysicalFile(result.FilePath, "application/octet-stream", Path.GetFileName(result.FilePath), enableRangeProcessing: true);
    }

    private static DateTime? NextFullRun(string frequency, string timeOfDay)
    {
        if (!TimeOnly.TryParse(timeOfDay, out var anchor)) return null;
        var now = DateTime.Now;
        if (frequency.Equals("Hourly", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = now.Date.AddHours(now.Hour).AddMinutes(anchor.Minute);
            return candidate <= now ? candidate.AddHours(1) : candidate;
        }
        if (frequency.Equals("Weekly", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = now.Date.Add(anchor.ToTimeSpan())
                .AddDays(((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7);
            return candidate <= now ? candidate.AddDays(7) : candidate;
        }
        var daily = now.Date.Add(anchor.ToTimeSpan());
        return daily <= now ? daily.AddDays(1) : daily;
    }

    private string DatabaseName()
    {
        var cs = _config.GetConnectionString("Default") ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(cs, @"(?:Database|Initial Catalog)=([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : "AFFLLPCaneFactory";
    }

    [HasPermission("Backup.Configure")]
    [HttpGet("script")]
    public async Task<IActionResult> GenerateScript()
    {
        var cfg = await _db.BackupConfigs.FirstOrDefaultAsync(b => !b.IsDeleted) ?? new BackupConfig();
        var db = DatabaseName().Replace("]", "]]", StringComparison.Ordinal);
        var folder = cfg.BackupFolderPath.Replace("'", "''", StringComparison.Ordinal);
        var sql = $@"-- CaneFactory FULL Backup Script (SQL Server Express compatible)
-- Run only on the SQL Server PC. The SQL Server service account needs Modify permission on this folder.
-- WITH COMPRESSION is intentionally omitted because SQL Server Express does not support it.
DECLARE @full NVARCHAR(4000) = N'{folder}\\{db}_FULL_' + FORMAT(GETDATE(),'yyyyMMdd_HHmmss') + N'.bak';
BACKUP DATABASE [{db}] TO DISK = @full WITH INIT, CHECKSUM;
RESTORE VERIFYONLY FROM DISK = @full WITH CHECKSUM;
";
        await _audit.LogAsync("BackupScriptGenerated", "Backup", "BackupConfig", cfg.Id.ToString());
        return File(System.Text.Encoding.UTF8.GetBytes(sql), "text/plain", "CaneFactoryBackup.sql");
    }

    [HasPermission("Backup.Configure")]
    [HttpGet("task-scheduler-xml")]
    public async Task<IActionResult> GenerateTaskSchedulerXml()
    {
        var cfg = await _db.BackupConfigs.FirstOrDefaultAsync(b => !b.IsDeleted) ?? new BackupConfig();
        var start = $"{DateTime.Today:yyyy-MM-dd}T{cfg.TimeOfDay}:00";
        var trigger = cfg.Frequency switch
        {
            "Hourly" => $@"<CalendarTrigger><StartBoundary>{start}</StartBoundary><Repetition><Interval>PT1H</Interval><Duration>P1D</Duration><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay><Enabled>true</Enabled></CalendarTrigger>",
            "Weekly" => $@"<CalendarTrigger><StartBoundary>{start}</StartBoundary><ScheduleByWeek><DaysOfWeek><Sunday/></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek><Enabled>true</Enabled></CalendarTrigger>",
            _ => $@"<CalendarTrigger><StartBoundary>{start}</StartBoundary><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay><Enabled>true</Enabled></CalendarTrigger>"
        };
        var scriptPath = SecurityElement.Escape(Path.Combine(cfg.BackupFolderPath, "CaneFactoryBackup.sql"));
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>CaneFactory {cfg.Frequency} SQL Server FULL backup fallback task.</Description></RegistrationInfo>
  <Triggers>{trigger}</Triggers>
  <Principals><Principal id=""Author""><RunLevel>HighestAvailable</RunLevel><LogonType>InteractiveToken</LogonType></Principal></Principals>
  <Settings><Enabled>{(cfg.Enabled ? "true" : "false")}</Enabled><StartWhenAvailable>true</StartWhenAvailable><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy></Settings>
  <Actions Context=""Author""><Exec><Command>sqlcmd.exe</Command><Arguments>-S localhost\SQLEXPRESS -E -i &quot;{scriptPath}&quot;</Arguments></Exec></Actions>
</Task>";
        return File(System.Text.Encoding.Unicode.GetBytes(xml), "application/xml", "CaneFactoryBackup-Task.xml");
    }
}
