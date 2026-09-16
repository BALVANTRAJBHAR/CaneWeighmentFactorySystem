using CaneFactory.API.Auth;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace CaneFactory.API.Controllers;

/// <summary>
/// Phase 13: SQL Server 2019 Express backup policy + generator. Express has no SQL Agent, so the
/// factory's Windows machine runs the generated .sql via Task Scheduler + sqlcmd on the configured
/// schedule (see docs/BACKUP_GUIDE.md). This API only stores the desired policy (single row) and
/// deterministically regenerates the matching script/schedule from it - it never touches disk or
/// SQL Server itself (the API container/DB user should never need backup/filesystem privileges).
/// </summary>
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

[ApiController]
[Authorize]
[Route("api/backup")]
public class BackupController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    private readonly IConfiguration _config;

    public BackupController(AppDbContext db, IAuditService audit, ICurrentUser current, IConfiguration config)
    {
        _db = db; _audit = audit; _current = current; _config = config;
    }

    [HasPermission("Backup.View")]
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig() => Ok(await _db.BackupConfigs.FirstOrDefaultAsync(b => !b.IsDeleted));

    [HasPermission("Backup.Configure")]
    [HttpPut("config")]
    public async Task<IActionResult> UpdateConfig([FromBody] BackupConfigDto src)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var validFrequency = new[] { "Daily", "Hourly", "Weekly" };
        if (!validFrequency.Contains(src.Frequency))
            return BadRequest(new { message = $"Frequency must be one of: {string.Join(", ", validFrequency)}" });
        if (!System.Text.RegularExpressions.Regex.IsMatch(src.TimeOfDay, @"^([01]\d|2[0-3]):[0-5]\d$"))
            return BadRequest(new { message = "TimeOfDay must be in 24-hour HH:mm format. Example: 02:00" });
        if (src.RetentionDays < 1) return BadRequest(new { message = "Retention Days must be at least 1." });
        if (string.IsNullOrWhiteSpace(src.BackupFolderPath)) return BadRequest(new { message = "Backup Folder Path is required. Example: E:\\Backup" });

        var b = await _db.BackupConfigs.FirstOrDefaultAsync(x => !x.IsDeleted);
        var isNew = b == null;
        b ??= new BackupConfig { CreatedBy = _current.UserId };
        var old = new { b.Enabled, b.Frequency, b.TimeOfDay, b.RetentionDays, b.BackupFolderPath };
        b.Enabled = src.Enabled; b.Frequency = src.Frequency; b.TimeOfDay = src.TimeOfDay;
        b.RetentionDays = src.RetentionDays; b.BackupFolderPath = src.BackupFolderPath.TrimEnd('\\', '/');
        b.DifferentialEnabled = src.DifferentialEnabled; b.DifferentialIntervalHours = Math.Clamp(src.DifferentialIntervalHours, 1, 24);
        b.TransactionLogEnabled = src.TransactionLogEnabled; b.TransactionLogIntervalMinutes = Math.Clamp(src.TransactionLogIntervalMinutes, 5, 1440);
        if (isNew) _db.BackupConfigs.Add(b);
        else { b.UpdatedAt = DateTime.UtcNow; b.UpdatedBy = _current.UserId; }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SystemSettingChange", "Backup", "BackupConfig", b.Id.ToString(), oldValue: old, newValue: src);
        return Ok(new { message = $"Backup policy saved: {b.Frequency} FULL backup at {b.TimeOfDay}, {b.RetentionDays}-day retention." });
    }

    private string DatabaseName()
    {
        var cs = _config.GetConnectionString("Default") ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(cs, @"Database=([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "AFFLLPCaneFactory";
    }

    /// <summary>Generates the FULL + DIFFERENTIAL + TRANSACTION LOG backup .sql script matching the
    /// current policy, including a retention-cleanup step. Save on the SQL Server host and run via
    /// Task Scheduler -> sqlcmd -S localhost\SQLEXPRESS -i CaneFactoryBackup.sql</summary>
    [HasPermission("Backup.Configure")]
    [HttpGet("script")]
    public async Task<IActionResult> GenerateScript()
    {
        var cfg = await _db.BackupConfigs.FirstOrDefaultAsync(b => !b.IsDeleted) ?? new BackupConfig();
        var db = DatabaseName();
        var folder = cfg.BackupFolderPath;
        var sql = $@"-- CaneFactory Backup Script (auto-generated from Backup Policy)
-- Database: {db}   Folder: {folder}   Retention: {cfg.RetentionDays} day(s)
-- Schedule via Windows Task Scheduler -> sqlcmd -S localhost\SQLEXPRESS -i CaneFactoryBackup.sql

DECLARE @full NVARCHAR(400) = N'{folder}\{db}_FULL_' + FORMAT(GETDATE(),'yyyyMMdd_HHmm') + N'.bak';
BACKUP DATABASE [{db}] TO DISK = @full WITH INIT, CHECKSUM, COMPRESSION;
RESTORE VERIFYONLY FROM DISK = @full WITH CHECKSUM;
{(cfg.DifferentialEnabled ? $@"
DECLARE @diff NVARCHAR(400) = N'{folder}\{db}_DIFF_' + FORMAT(GETDATE(),'yyyyMMdd_HHmm') + N'.bak';
BACKUP DATABASE [{db}] TO DISK = @diff WITH DIFFERENTIAL, INIT, CHECKSUM;" : "")}
{(cfg.TransactionLogEnabled ? $@"
DECLARE @log NVARCHAR(400) = N'{folder}\{db}_LOG_' + FORMAT(GETDATE(),'yyyyMMdd_HHmm') + N'.trn';
BACKUP LOG [{db}] TO DISK = @log WITH INIT, CHECKSUM;" : "")}

-- Retention cleanup (requires xp_cmdshell enabled, or run the equivalent PowerShell scheduled task instead):
-- EXEC master..xp_cmdshell 'forfiles /p ""{folder}"" /s /m *.bak /d -{cfg.RetentionDays} /c ""cmd /c del @path""';
-- EXEC master..xp_cmdshell 'forfiles /p ""{folder}"" /s /m *.trn /d -{cfg.RetentionDays} /c ""cmd /c del @path""';
";
        await _audit.LogAsync("BackupScriptGenerated", "Backup", "BackupConfig", cfg.Id.ToString());
        return File(System.Text.Encoding.UTF8.GetBytes(sql), "text/plain", "CaneFactoryBackup.sql");
    }

    /// <summary>Generates a Windows Task Scheduler XML importable via `schtasks /Create /XML` to run
    /// the backup script on the configured Frequency/TimeOfDay without any SQL Agent (Express-safe).</summary>
    [HasPermission("Backup.Configure")]
    [HttpGet("task-scheduler-xml")]
    public async Task<IActionResult> GenerateTaskSchedulerXml()
    {
        var cfg = await _db.BackupConfigs.FirstOrDefaultAsync(b => !b.IsDeleted) ?? new BackupConfig();
        var timeParts = (cfg.TimeOfDay ?? "02:00").Split(':');
        var trigger = cfg.Frequency switch
        {
            "Hourly" => $@"<TimeTrigger><StartBoundary>2026-01-01T{cfg.TimeOfDay}:00</StartBoundary><Repetition><Interval>PT1H</Interval></Repetition><Enabled>true</Enabled></TimeTrigger>",
            "Weekly" => $@"<CalendarTrigger><StartBoundary>2026-01-01T{cfg.TimeOfDay}:00</StartBoundary><ScheduleByWeek><DaysOfWeek><Sunday/></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek><Enabled>true</Enabled></CalendarTrigger>",
            _ => $@"<CalendarTrigger><StartBoundary>2026-01-01T{cfg.TimeOfDay}:00</StartBoundary><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay><Enabled>true</Enabled></CalendarTrigger>"
        };
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>CaneFactory automated {cfg.Frequency} SQL Server backup ({cfg.RetentionDays}-day retention)</Description></RegistrationInfo>
  <Triggers>{trigger}</Triggers>
  <Principals><Principal id=""Author""><RunLevel>HighestAvailable</RunLevel><LogonType>S4U</LogonType></Principal></Principals>
  <Settings><Enabled>{(cfg.Enabled ? "true" : "false")}</Enabled><StartWhenAvailable>true</StartWhenAvailable></Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>sqlcmd.exe</Command>
      <Arguments>-S localhost\SQLEXPRESS -i ""{cfg.BackupFolderPath}\CaneFactoryBackup.sql""</Arguments>
    </Exec>
  </Actions>
</Task>";
        return File(System.Text.Encoding.Unicode.GetBytes(xml), "application/xml", "CaneFactoryBackup-Task.xml");
    }
}
