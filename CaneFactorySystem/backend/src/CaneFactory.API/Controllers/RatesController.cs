using CaneFactory.API.Auth;
using CaneFactory.Application.Common;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

[ApiController]
[Authorize]
[Route("api/rates")]
public class RatesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    public RatesController(AppDbContext db, IAuditService audit, ICurrentUser current)
    {
        _db = db; _audit = audit; _current = current;
    }

    private IActionResult? Deny(string action) =>
        _current.HasPermission($"Rate.{action}") ? null
            : StatusCode(403, new { message = $"You do not have 'Rate.{action}' permission." });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    {
        if (Deny("View") is { } d) return d;
        var q = _db.Rates.Where(r => !r.IsDeleted);
        if (!includeInactive) q = q.Where(r => r.Status);
        var items = await q.OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new
            {
                r.Id, r.VarietyTypeId, VarietyTypeName = r.VarietyType.VarietyTypeName,
                r.Rate, r.EffectiveFrom, r.EffectiveTo, r.Status, r.CreatedAt
            }).ToListAsync();
        return Ok(new { items, totalCount = items.Count });
    }

    [HttpGet("current/{varietyTypeId:int}")]
    public async Task<IActionResult> Current(int varietyTypeId)
    {
        if (Deny("View") is { } d) return d;
        var today = DateTime.UtcNow.Date;
        var rate = await _db.Rates.Where(r => r.VarietyTypeId == varietyTypeId && !r.IsDeleted && r.Status
                && r.EffectiveFrom.Date <= today
                && (r.EffectiveTo == null || r.EffectiveTo.Value.Date >= today))
            .OrderByDescending(r => r.EffectiveFrom).FirstOrDefaultAsync();
        return rate == null
            ? NotFound(new { message = "No active rate exists for this Variety Type. Please configure Rate Master first." })
            : Ok(new { rate.Id, rate.Rate, rate.EffectiveFrom, rate.EffectiveTo });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RateMaster req)
    {
        if (Deny("Create") is { } d) return d;
        if (req.Rate <= 0) return BadRequest(new { message = "Rate must be greater than 0. Example: 375.00" });
        if (!await _db.VarietyTypes.AnyAsync(v => v.Id == req.VarietyTypeId && !v.IsDeleted && v.Status))
            return BadRequest(new { message = "Selected Variety Type does not exist or is inactive." });
        var effectiveFrom = req.EffectiveFrom.Date;
        var effectiveTo = req.EffectiveTo?.Date;
        if (effectiveTo.HasValue && effectiveTo < effectiveFrom)
            return BadRequest(new { message = "Effective To cannot be before Effective From." });

        // No overlapping active period for same VarietyType
        var overlap = await _db.Rates.AnyAsync(r => r.VarietyTypeId == req.VarietyTypeId && !r.IsDeleted && r.Status
            && r.EffectiveFrom.Date <= (effectiveTo ?? DateTime.MaxValue.Date)
            && (r.EffectiveTo == null || r.EffectiveTo.Value.Date >= effectiveFrom));
        if (overlap)
            return Conflict(new { message = "An active rate already overlaps this effective period for the selected Variety Type. Close the previous rate first." });

        var rate = new RateMaster
        {
            VarietyTypeId = req.VarietyTypeId,
            Rate = WeightCalculator.R2(req.Rate),
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            CreatedBy = _current.UserId
        };
        _db.Rates.Add(rate);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("RateModification", "Rate", "RateMaster", rate.Id.ToString(), newValue: rate);
        return Ok(new { message = $"Rate {rate.Rate:F2} saved successfully (effective from {rate.EffectiveFrom:dd-MM-yyyy}).", id = rate.Id });
    }

    /// <summary>Close a rate period (rates are versioned; historical rates are never edited in place).</summary>
    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> Close(int id, [FromBody] Dictionary<string, DateTime> body)
    {
        if (Deny("Edit") is { } d) return d;
        var rate = await _db.Rates.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (rate == null) return NotFound(new { message = "Rate not found." });
        var to = body.GetValueOrDefault("effectiveTo", DateTime.UtcNow).Date;
        var old = new { rate.EffectiveTo };
        rate.EffectiveTo = to;
        rate.UpdatedAt = DateTime.UtcNow;
        rate.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("RateModification", "Rate", "RateMaster", id.ToString(), oldValue: old, newValue: new { rate.EffectiveTo });
        return Ok(new { message = $"Rate period closed on {to:dd-MM-yyyy HH:mm}." });
    }

    // ---------------- Controlled UNPAID rate recalculation (preview -> confirm) ----------------

    [HttpGet("recalculate/preview")]
    public async Task<IActionResult> RecalculatePreview([FromQuery] int varietyTypeId, [FromQuery] decimal newRate)
    {
        if (Deny("Approve") is { } d) return d;
        var affected = await UnpaidQuery(varietyTypeId).Select(p => new
        {
            p.Id, p.GrowerCode, p.FinalWeightQuintal, OldRate = p.Rate, OldAmount = p.PurchaseAmount,
            NewRate = newRate,
            NewAmount = p.FinalWeightQuintal == null ? null : (decimal?)Math.Round(p.FinalWeightQuintal.Value * newRate, 2)
        }).ToListAsync();
        return Ok(new { count = affected.Count, records = affected,
            note = "Only UNPAID, unlocked, non-cancelled purchases are listed. Paid history is never modified." });
    }

    [HttpPost("recalculate/apply")]
    public async Task<IActionResult> RecalculateApply([FromBody] Dictionary<string, decimal> body)
    {
        if (Deny("Approve") is { } d) return d;
        var varietyTypeId = (int)body.GetValueOrDefault("varietyTypeId");
        var newRate = WeightCalculator.R2(body.GetValueOrDefault("newRate"));
        if (newRate <= 0) return BadRequest(new { message = "New rate must be greater than 0." });

        var purchases = await UnpaidQuery(varietyTypeId).ToListAsync();
        foreach (var p in purchases)
        {
            var old = new { p.Rate, p.PurchaseAmount };
            p.Rate = newRate;
            if (p.FinalWeightQuintal.HasValue)
                p.PurchaseAmount = WeightCalculator.R2(p.FinalWeightQuintal.Value * newRate);
            p.UpdatedAt = DateTime.UtcNow;
            p.UpdatedBy = _current.UserId;
            await _audit.LogAsync("UnpaidRateRecalculation", "Rate", "Purchase", p.Id.ToString(),
                oldValue: old, newValue: new { p.Rate, p.PurchaseAmount });
        }
        await _db.SaveChangesAsync();
        return Ok(new { message = $"Rate recalculated for {purchases.Count} unpaid purchase(s). All changes audited." });
    }

    private IQueryable<Purchase> UnpaidQuery(int varietyTypeId) =>
        _db.Purchases.Where(p => p.VarietyTypeId == varietyTypeId && !p.IsDeleted
            && p.PaymentFlag == "N" && p.LockStatus == "UNLOCKED" && p.GrossTareStatus != "CANCELLED");
}
