using CaneFactory.API.Auth;
using CaneFactory.API.Services;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

[ApiController]
[Authorize]
[Route("api/sale-item-rates")]
public class SaleItemRatesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    public SaleItemRatesController(AppDbContext db, IAuditService audit, ICurrentUser current) => (_db, _audit, _current) = (db, audit, current);

    private IActionResult? Deny(string action) => _current.HasPermission($"Rate.{action}")
        ? null : StatusCode(403, new { message = $"You do not have 'Rate.{action}' permission." });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    {
        if (Deny("View") is { } denied) return denied;
        var items = await SaleItemRateStore.ListAsync(_db, includeInactive, HttpContext.RequestAborted);
        return Ok(new { items, totalCount = items.Count });
    }

    [HttpGet("current/{itemId:int}")]
    public async Task<IActionResult> Current(int itemId)
    {
        if (Deny("View") is { } denied) return denied;
        var rate = await SaleItemRateStore.CurrentAsync(_db, itemId, DateTime.UtcNow.Date, HttpContext.RequestAborted);
        return rate == null
            ? NotFound(new { message = "No active Sale Rate exists for this Item. Configure Sale Rates first." })
            : Ok(rate);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaleItemRateRequest request)
    {
        if (Deny("Create") is { } denied) return denied;
        if (request.ItemId <= 0 || request.Rate <= 0)
            return BadRequest(new { message = "Item and a rate greater than zero are required." });
        if (!await _db.Items.AnyAsync(item => item.Id == request.ItemId && !item.IsDeleted && item.Status))
            return BadRequest(new { message = "Selected Item does not exist or is inactive." });
        var from = request.EffectiveFrom.Date;
        var to = request.EffectiveTo?.Date;
        if (to.HasValue && to < from) return BadRequest(new { message = "Effective To cannot be before Effective From." });

        var existing = await SaleItemRateStore.ListAsync(_db, includeInactive: true, HttpContext.RequestAborted);
        var conflicts = existing.Where(rate => rate.ItemId == request.ItemId && rate.Status &&
                rate.EffectiveFrom.Date <= (to ?? DateTime.MaxValue.Date) &&
                (rate.EffectiveTo == null || rate.EffectiveTo.Value.Date >= from))
            .OrderBy(rate => rate.EffectiveFrom).ToList();
        if (conflicts.Any(rate => rate.EffectiveFrom.Date >= from))
            return Conflict(new { message = "A Sale Rate already starts on or after this Effective From date. Choose a later date or close that scheduled period first." });

        await _db.ExecuteInTransactionAsync(async () =>
        {
            foreach (var previous in conflicts)
                await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SaleItemRates SET EffectiveTo = {from.AddDays(-1)}, UpdatedAt = {DateTime.UtcNow}, UpdatedBy = {_current.UserId} WHERE Id = {previous.Id}");
            await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SaleItemRates (ItemId, Rate, EffectiveFrom, EffectiveTo, CreatedAt, CreatedBy, Status, IsDeleted) VALUES ({request.ItemId}, {Math.Round(request.Rate, 2)}, {from}, {to}, {DateTime.UtcNow}, {_current.UserId}, {true}, {false})");
        });
        var created = (await SaleItemRateStore.ListAsync(_db, true, HttpContext.RequestAborted))
            .First(rate => rate.ItemId == request.ItemId && rate.EffectiveFrom.Date == from && rate.Rate == Math.Round(request.Rate, 2));
        await _audit.LogAsync("SaleItemRatePeriodCreated", "Rate", "SaleItemRate", created.Id.ToString(), newValue: created);
        return Ok(new { message = $"Sale Rate {created.Rate:F2} saved successfully (effective from {created.EffectiveFrom:dd-MM-yyyy}).", id = created.Id });
    }
}

public class SaleItemRateRequest
{
    public int ItemId { get; set; }
    public decimal Rate { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}
