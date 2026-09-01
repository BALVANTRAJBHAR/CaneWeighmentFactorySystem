using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

[ApiController]
[Authorize]
[Route("api/purchases")]
public class PurchasesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    public PurchasesController(AppDbContext db, IAuditService audit, ICurrentUser current)
    {
        _db = db; _audit = audit; _current = current;
    }

    private IActionResult? Deny(string action) =>
        _current.HasPermission($"Purchase.{action}") ? null
            : StatusCode(403, new { message = $"You do not have 'Purchase.{action}' permission." });

    private bool IsFarmerOnly =>
        (_current.Role ?? "").Split(',').All(r => r is "Farmer" or "") && (_current.Role ?? "") != "";

    /// <summary>Farmer accounts see ONLY their own purchases (object-ownership enforcement).</summary>
    private async Task<IQueryable<Domain.Entities.Purchase>> ScopedQueryAsync()
    {
        var q = _db.Purchases.Where(p => !p.IsDeleted);
        if (!IsFarmerOnly) return q;
        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == _current.UserId);
        return q.Where(p => p.Grower.Mobile == user.Mobile);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? growerCode, [FromQuery] string? status,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (Deny("View") is { } d) return d;
        var q = await ScopedQueryAsync();
        if (!string.IsNullOrWhiteSpace(growerCode)) q = q.Where(p => p.GrowerCode == growerCode.Trim());
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.GrossTareStatus == status);
        if (from.HasValue) q = q.Where(p => p.GrossDateTime >= from);
        if (to.HasValue) q = q.Where(p => p.GrossDateTime <= to);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                purchaseId = p.Id, p.GrowerCode, GrowerName = p.Grower.GrowerName,
                FatherName = p.Grower.FatherName, VillageName = p.Grower.Village.VillageName,
                p.VehicleNumber, VehicleTypeName = p.VehicleType.VehicleTypeName,
                VarietyName = p.Variety.VarietyName,
                p.GrossWeightQuintal, p.GrossDateTime, p.GrossByUserName,
                p.TareWeightQuintal, p.TareDateTime, p.TareByUserName,
                p.NetWeightQuintal, p.CuttingPercent, p.CuttingWeightQuintal,
                p.TaxPercent, p.TaxWeightQuintal, p.FinalWeightQuintal,
                p.Rate, p.PurchaseAmount, p.GrossTareStatus, p.PaymentStatus, p.LockStatus, p.AdviceNumber
            }).ToListAsync();
        return Ok(new { items, totalCount = total, page, pageSize });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (Deny("View") is { } d) return d;
        var q = await ScopedQueryAsync();
        var p = await q.Where(x => x.Id == id).Select(p => new
        {
            purchaseId = p.Id, p.GrowerCode, GrowerName = p.Grower.GrowerName,
            FatherName = p.Grower.FatherName, VillageName = p.Grower.Village.VillageName,
            p.VehicleNumber, VarietyName = p.Variety.VarietyName,
            p.ScaleReadingGrossKg, p.GrossWeightQuintal, p.GrossDateTime, p.GrossByUserName,
            p.ScaleReadingTareKg, p.TareWeightQuintal, p.TareDateTime, p.TareByUserName,
            p.NetWeightQuintal, p.CuttingPercent, p.CuttingWeightQuintal,
            p.TaxPercent, p.TaxWeightQuintal, p.FinalWeightQuintal,
            p.Rate, p.PurchaseAmount, p.GrossTareStatus, p.PaymentStatus, p.LockStatus,
            p.AdviceNumber, SeasonName = p.Season.SeasonName
        }).FirstOrDefaultAsync();
        // 404 (not 403) for out-of-scope IDs: does not leak other farmers' record existence
        return p == null ? NotFound(new { message = "Purchase not found." }) : Ok(p);
    }

    [HttpPost("{id:int}/lock")]
    public async Task<IActionResult> Lock(int id) => await SetLock(id, "LOCKED", "Lock");

    [HttpPost("{id:int}/unlock")]
    public async Task<IActionResult> Unlock(int id) => await SetLock(id, "UNLOCKED", "Unlock");

    private async Task<IActionResult> SetLock(int id, string state, string action)
    {
        if (Deny(action) is { } d) return d;
        var p = await _db.Purchases.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return NotFound(new { message = "Purchase not found." });
        if (p.LockStatus == state) return Conflict(new { message = $"Purchase {id} is already {state}." });
        var old = p.LockStatus;
        p.LockStatus = state;
        p.UpdatedAt = DateTime.UtcNow;
        p.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(action, "Purchase", "Purchase", id.ToString(), oldValue: old, newValue: state);
        return Ok(new { message = $"Purchase {id} {state.ToLower()} successfully." });
    }

    /// <summary>Controlled cancellation - transactional records are never hard-deleted.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] Dictionary<string, string> body)
    {
        if (Deny("Cancel") is { } d) return d;
        var reason = (body.GetValueOrDefault("reason") ?? "").Trim();
        if (reason.Length < 5) return BadRequest(new { message = "A cancellation reason (min 5 characters) is required." });
        var p = await _db.Purchases.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return NotFound(new { message = "Purchase not found." });
        if (p.PaymentFlag == "Y") return Conflict(new { message = "A PAID purchase cannot be cancelled. Use payment reversal first." });
        if (p.GrossTareStatus == "CANCELLED") return Conflict(new { message = "Purchase is already cancelled." });
        if (p.LockStatus == "LOCKED") return Conflict(new { message = "Purchase is LOCKED. Unlock before cancelling." });
        var old = p.GrossTareStatus;
        p.GrossTareStatus = "CANCELLED";
        p.PaymentStatus = "CANCELLED";
        p.UpdatedAt = DateTime.UtcNow;
        p.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Cancel", "Purchase", "Purchase", id.ToString(), oldValue: old,
            newValue: new { Status = "CANCELLED", Reason = reason });
        return Ok(new { message = $"Purchase {id} cancelled. Reason recorded in audit log." });
    }
}
