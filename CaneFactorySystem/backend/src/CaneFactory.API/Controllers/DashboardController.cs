using CaneFactory.API.Auth;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Weighing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

[ApiController]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _db;
    public AuditController(AppDbContext db) => _db = db;

    [HasPermission("Audit.View")]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? module, [FromQuery] string? action,
        [FromQuery] string? username, [FromQuery] string? entity, [FromQuery] string? entityId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(module)) q = q.Where(a => a.Module == module);
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(username)) q = q.Where(a => a.Username == username);
        if (!string.IsNullOrWhiteSpace(entity)) q = q.Where(a => a.Entity == entity);
        if (!string.IsNullOrWhiteSpace(entityId)) q = q.Where(a => a.EntityId == entityId);
        if (from.HasValue) q = q.Where(a => a.Timestamp >= from);
        if (to.HasValue) q = q.Where(a => a.Timestamp <= to);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.Timestamp).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { items, totalCount = total, page, pageSize });
    }
}

[ApiController]
[Authorize]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly WeighingService _weighing;
    public DashboardController(AppDbContext db, ICurrentUser current, WeighingService weighing)
    {
        _db = db; _current = current; _weighing = weighing;
    }

    /// <summary>Common dashboard header info. Location/temperature are fetched client-side from
    /// configured services only when internet is available (per offline-first requirement).</summary>
    [HttpGet("header")]
    public async Task<IActionResult> Header()
    {
        var company = await _db.CompanyConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        var season = await _db.Seasons.AsNoTracking().FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted);
        return Ok(new
        {
            companyName = company?.CompanyName,
            address = company?.Address,
            serverTimeUtc = DateTime.UtcNow,
            username = _current.Username,
            roles = _current.Role,
            season = season?.SeasonName
        });
    }

    [HasPermission("Dashboard.View")]
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var today = DateTime.UtcNow.Date;
        var purchases = _db.Purchases.Where(p => !p.IsDeleted && p.GrossDateTime >= today);
        return Ok(new
        {
            todayVehicles = await purchases.CountAsync(),
            pendingTare = await _db.Purchases.CountAsync(p => p.GrossTareStatus == "GROSS_DONE" && !p.IsDeleted),
            todayFinalWeightQuintal = (await purchases.Where(p => p.FinalWeightQuintal != null).Select(p => p.FinalWeightQuintal).ToListAsync()).Sum(x => x ?? 0),
            paymentPending = await _db.Purchases.CountAsync(p => p.PaymentStatus == "PENDING" && !p.IsDeleted),
            totalGrowers = await _db.Growers.CountAsync(g => !g.IsDeleted && g.Status),
            totalVillages = await _db.Villages.CountAsync(v => !v.IsDeleted && v.Status)
        });
    }

    /// <summary>Role-aware health: each caller receives only the health items its role should see.</summary>
    [HasPermission("Health.View")]
    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        var roles = (_current.Role ?? "").Split(',');
        bool dev = roles.Contains("Developer");
        bool admin = roles.Contains("Admin") || dev;
        bool op = roles.Contains("Operator") || roles.Contains("SalePurchase") || admin;
        bool accountant = roles.Contains("Accountant") || admin;

        var items = new List<object>();
        (string, string) Level(double v, double warn, double crit) =>
            v >= crit ? ("Red", "Critical") : v >= warn ? ("Yellow", "Warning") : ("Green", "Normal");

        double? cpu = null, ram = null;
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var mem = System.IO.File.ReadAllLines("/proc/meminfo");
                var total = double.Parse(mem[0].Split(':')[1].Trim().Split(' ')[0]);
                var avail = double.Parse(mem[2].Split(':')[1].Trim().Split(' ')[0]);
                ram = Math.Round((1 - avail / total) * 100, 1);
            }
            else
            {
                ram = Math.Round(Environment.WorkingSet / (1024.0 * 1024 * 1024), 1);
            }
        }
        catch { /* best effort */ }

        double storagePct = 0;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory) ?? "/");
            storagePct = Math.Round((1 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100, 1);
        }
        catch { /* best effort */ }

        bool dbOk;
        try { dbOk = await _db.Database.CanConnectAsync(); } catch { dbOk = false; }

        if (dev || admin)
        {
            if (ram.HasValue) items.Add(new { name = "RAM", value = $"{ram}%", color = Level(ram.Value, 70, 90).Item1 });
            items.Add(new { name = "Storage", value = $"{storagePct}%", color = Level(storagePct, 75, 85).Item1 });
            if (cpu.HasValue) items.Add(new { name = "CPU", value = $"{cpu}%", color = Level(cpu.Value, 70, 90).Item1 });
        }
        if (op || accountant || dev)
            items.Add(new { name = "Database", value = dbOk ? "Connected" : "Down", color = dbOk ? "Green" : "Red" });
        if (op)
        {
            var w = _weighing.Current;
            items.Add(new { name = "Digitizer", value = w.DeviceConnected ? $"Connected ({w.DeviceName})" : "Disconnected", color = w.DeviceConnected ? "Green" : "Red" });
            var cams = await _db.Cameras.CountAsync(c => !c.IsDeleted && c.Status);
            items.Add(new { name = "Cameras", value = $"{cams} configured", color = cams > 0 ? "Green" : "Yellow" });
            var print = await _db.PrintConfigs.AsNoTracking().FirstOrDefaultAsync(p => !p.IsDeleted);
            items.Add(new { name = "Printer", value = print?.PrinterName ?? "Not configured", color = print != null ? "Green" : "Yellow" });
        }
        if (dev)
        {
            var sms = await _db.SmsConfigs.AsNoTracking().FirstOrDefaultAsync(s => !s.IsDeleted);
            items.Add(new { name = "SMS", value = sms?.Enabled == true ? "Enabled" : "Disabled", color = sms?.Enabled == true ? "Green" : "Yellow" });
            var backup = await _db.BackupConfigs.AsNoTracking().FirstOrDefaultAsync(b => !b.IsDeleted);
            items.Add(new { name = "Backup Schedule", value = backup?.Enabled == true ? $"{backup.Frequency} @ {backup.TimeOfDay}" : "Disabled", color = backup?.Enabled == true ? "Green" : "Red" });
        }
        // Farmer receives only basic service status
        if (!dev && !admin && !op && !accountant)
            items.Add(new { name = "Service", value = "Online", color = "Green" });

        return Ok(new { items });
    }
}
