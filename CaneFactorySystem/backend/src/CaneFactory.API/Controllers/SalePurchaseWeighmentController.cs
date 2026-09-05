using CaneFactory.Application.Common;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Weighing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CaneFactory.API.Controllers;

/// <summary>
/// Standalone, non-cane sale/purchase weighment lifecycle.  It intentionally never writes to
/// Purchases, PaymentPurchases, or Payments: those are cane purchase/payment records.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sale-purchase-weighment")]
public class SalePurchaseWeighmentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAuditService _audit;
    private readonly ISequenceGenerator _sequence;
    private readonly WeighingService _weighing;
    private readonly ISmsService _sms;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;

    public SalePurchaseWeighmentController(AppDbContext db, ICurrentUser current, IAuditService audit,
        ISequenceGenerator sequence, WeighingService weighing, ISmsService sms, IServiceScopeFactory scopeFactory, IMemoryCache cache)
    {
        _db = db; _current = current; _audit = audit; _sequence = sequence; _weighing = weighing; _sms = sms; _scopeFactory = scopeFactory; _cache = cache;
    }

    private IActionResult? Deny(string action) => _current.HasPermission($"SalePurchase.{action}")
        ? null : StatusCode(403, new { message = $"You do not have 'SalePurchase.{action}' permission." });

    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        if (Deny("View") is { } denied) return denied;
        var rows = await _db.SalePurchases.AsNoTracking()
            .Where(x => !x.IsDeleted && x.WeighmentStatus == "TARE_PENDING_GROSS")
            .OrderBy(x => x.TareDateTime)
            .Select(x => new
            {
                salePurchaseId = x.Id, Item = x.Item.ItemName, Party = x.Party.PartyName,
                VehicleType = x.VehicleType.VehicleTypeName, x.VehicleNumber, x.DriverName,
                x.TareWeightQuintal, x.TareDateTime, Status = x.WeighmentStatus
            }).ToListAsync();
        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetForGross(int id)
    {
        if (Deny("View") is { } denied) return denied;
        var row = await _db.SalePurchases.AsNoTracking().Where(x => x.Id == id && !x.IsDeleted)
            .Select(x => new
            {
                salePurchaseId = x.Id, itemId = x.ItemId, Item = x.Item.ItemName, partyId = x.PartyId,
                Party = x.Party.PartyName, partyMobile = x.Party.Mobile, vehicleTypeId = x.VehicleTypeId,
                VehicleType = x.VehicleType.VehicleTypeName, x.VehicleNumber, x.DriverName, x.Remark,
                x.TareWeightQuintal, x.TareDateTime, TareOperator = x.TareByUserName, Status = x.WeighmentStatus,
                x.GrossWeightQuintal, x.FinalWeightQuintal, x.Rate, x.Amount
            }).FirstOrDefaultAsync();
        if (row == null) return NotFound(new { message = $"SalePurchase ID {id} does not exist." });
        if (row.Status == "CANCELLED") return Conflict(new { message = $"SalePurchase {id} is cancelled." });
        if (row.Status != "TARE_PENDING_GROSS")
            return Conflict(new { message = $"SalePurchase {id} is already completed and cannot be gross-weighed again." });
        return Ok(row);
    }

    [HttpPost("tare")]
    public async Task<IActionResult> SaveTare(SalePurchaseTareSaveRequest req)
    {
        if (Deny("Create") is { } denied) return denied;
        if (Duplicate(req.IdempotencyKey, out var duplicate)) return duplicate!;
        if (!_weighing.TryGetUsableWeight(out var liveKg, out var deviceError))
            return Conflict(new { message = deviceError });
        if (req.ItemId <= 0 || req.PartyId <= 0 || req.VehicleTypeId <= 0)
            return BadRequest(new { message = "Item, Party and Vehicle Type are required." });
        var vehicle = Validators.NormUpper(req.VehicleNumber);
        var driver = Validators.Norm(req.DriverName);
        if (vehicle.Length is < 4 or > 15) return BadRequest(new { message = "Vehicle Number must be 4-15 characters." });
        if (driver.Length < 2 || driver.Length > 100) return BadRequest(new { message = "Driver Name must be 2-100 characters." });

        var item = await _db.Items.AnyAsync(x => x.Id == req.ItemId && !x.IsDeleted && x.Status);
        var party = await _db.Parties.FirstOrDefaultAsync(x => x.Id == req.PartyId && !x.IsDeleted && x.Status);
        var vehicleType = await _db.VehicleTypes.AnyAsync(x => x.Id == req.VehicleTypeId && !x.IsDeleted && x.Status);
        if (!item || party == null || !vehicleType) return BadRequest(new { message = "Selected Item, Party or Vehicle Type is inactive or invalid." });

        var tare = WeightCalculator.KgToQuintal(liveKg);
        if (tare <= 0) return Conflict(new { message = "Live tare weight must be greater than zero." });
        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync();
        var id = (int)await _sequence.NextAsync("SalePurchaseId", 1);
        var record = new SalePurchase
        {
            Id = id, ItemId = req.ItemId, PartyId = req.PartyId, VehicleTypeId = req.VehicleTypeId,
            VehicleNumber = vehicle, DriverName = driver, Remark = Validators.Norm(req.Remark),
            ScaleReadingTareKg = WeightCalculator.R2(liveKg), TareWeightQuintal = tare,
            TareDateTime = now, TareByUserId = _current.UserId!.Value, TareByUserName = _current.Username ?? "",
            WeighmentStatus = "TARE_PENDING_GROSS", CreatedBy = _current.UserId
        };
        _db.SalePurchases.Add(record);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("TareSave", "SalePurchase", "SalePurchase", id.ToString(), newValue: new
        { record.ItemId, record.PartyId, record.VehicleNumber, record.TareWeightQuintal, record.TareDateTime });
        QueueCapture(id, "TARE");
        return Ok(new { message = $"Tare saved. SalePurchase ID: {id}. Pending gross weighment.", salePurchaseId = id,
            tareWeightQuintal = tare, status = record.WeighmentStatus, autoPrint = await AutoPrintAsync(id, "TARE") });
    }

    [HttpPost("gross")]
    public async Task<IActionResult> SaveGross(SalePurchaseGrossSaveRequest req)
    {
        if (Deny("Edit") is { } denied) return denied;
        if (Duplicate(req.IdempotencyKey, out var duplicate)) return duplicate!;
        if (!_weighing.TryGetUsableWeight(out var liveKg, out var deviceError))
            return Conflict(new { message = deviceError });
        if (req.Rate is < 0) return BadRequest(new { message = "Rate cannot be negative." });

        // Serializable transaction makes state validation + completion atomic across two operators.
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var record = await _db.SalePurchases.FirstOrDefaultAsync(x => x.Id == req.SalePurchaseId && !x.IsDeleted);
        if (record == null) return NotFound(new { message = $"SalePurchase ID {req.SalePurchaseId} does not exist." });
        if (record.WeighmentStatus == "CANCELLED") return Conflict(new { message = "Cancelled SalePurchase cannot be processed." });
        if (record.WeighmentStatus != "TARE_PENDING_GROSS") return Conflict(new { message = "Gross is already completed for this SalePurchase." });

        var gross = WeightCalculator.KgToQuintal(liveKg);
        if (gross <= 0) return Conflict(new { message = "Live gross weight must be greater than zero." });
        var finalWeight = WeightCalculator.R2(gross - record.TareWeightQuintal);
        if (finalWeight < 0) return Conflict(new { message = "Gross weight cannot be less than tare weight." });
        record.ScaleReadingGrossKg = WeightCalculator.R2(liveKg);
        record.GrossWeightQuintal = gross;
        record.FinalWeightQuintal = finalWeight;
        record.GrossDateTime = DateTime.UtcNow;
        record.GrossByUserId = _current.UserId;
        record.GrossByUserName = _current.Username ?? "";
        record.Rate = req.Rate.HasValue ? WeightCalculator.R2(req.Rate.Value) : null;
        record.Amount = record.Rate.HasValue ? WeightCalculator.R2(finalWeight * record.Rate.Value) : null;
        record.WeighmentStatus = "COMPLETED";
        record.UpdatedAt = DateTime.UtcNow;
        record.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _audit.LogAsync("GrossSave", "SalePurchase", "SalePurchase", record.Id.ToString(), newValue: new
        { record.GrossWeightQuintal, record.TareWeightQuintal, record.FinalWeightQuintal, record.Rate, record.Amount });
        QueueCapture(record.Id, "GROSS");
        var party = await _db.Parties.AsNoTracking().Where(x => x.Id == record.PartyId)
            .Select(x => new { x.PartyName, x.Mobile, x.Status, x.IsDeleted }).FirstAsync();
        var configuredRecipients = await _db.SmsConfigs.AsNoTracking().Where(x => !x.IsDeleted && x.Enabled)
            .Select(x => x.SalePurchaseRecipients).FirstOrDefaultAsync() ?? "";
        var recipients = configuredRecipients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length == 10 && x.All(char.IsDigit)).Distinct().ToList();
        var placeholders = new Dictionary<string, string>
            {
                ["SalePurchaseId"] = record.Id.ToString(), ["PartyName"] = party.PartyName, ["FinalWeight"] = finalWeight.ToString("F2"),
                ["Amount"] = record.Amount?.ToString("F2") ?? ""
            };
        // A Party is mandatory for the transaction; successful-event SMS is additionally gated by
        // explicit configured recipient numbers, never implicitly sent to an arbitrary party mobile.
        var queued = party.Status && !party.IsDeleted
            ? await Task.WhenAll(recipients.Select((mobile, index) => _sms.QueueForPartyAsync("SALE_PURCHASE_COMPLETED",
                record.PartyId, mobile, $"SP-{record.Id}-{index}", placeholders)))
            : Array.Empty<bool>();
        var smsQueued = queued.Any(x => x);
        return Ok(new { message = $"SalePurchase {record.Id} completed successfully. Final Weight: {finalWeight:F2} Quintal.",
            salePurchaseId = record.Id, grossWeightQuintal = gross, finalWeightQuintal = finalWeight,
            amount = record.Amount, status = record.WeighmentStatus, smsQueued, autoPrint = await AutoPrintAsync(record.Id, "GROSS") });
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] Dictionary<string, string> body)
    {
        if (Deny("Cancel") is { } denied) return denied;
        var reason = Validators.Norm(body.GetValueOrDefault("reason"));
        if (reason.Length < 5) return BadRequest(new { message = "Cancellation reason must be at least 5 characters." });
        var record = await _db.SalePurchases.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (record == null) return NotFound(new { message = "SalePurchase not found." });
        if (record.WeighmentStatus == "COMPLETED") return Conflict(new { message = "Completed SalePurchase cannot be cancelled; use a documented reversal." });
        if (record.WeighmentStatus == "CANCELLED") return Conflict(new { message = "SalePurchase is already cancelled." });
        record.WeighmentStatus = "CANCELLED"; record.CancelReason = reason; record.UpdatedAt = DateTime.UtcNow; record.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Cancel", "SalePurchase", "SalePurchase", id.ToString(), newValue: new { reason });
        return Ok(new { message = $"SalePurchase {id} cancelled." });
    }

    private async Task<object?> AutoPrintAsync(int id, string stage)
    {
        var cfg = await _db.PrintConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        if (cfg == null || !cfg.AutoPrint || cfg.SalePurchaseCopies <= 0) return null;
        return new { printerType = cfg.PrinterType, printerName = cfg.PrinterName, copies = cfg.SalePurchaseCopies,
            language = cfg.Language, documentUrl = $"/api/print/sale-purchase/{id}?stage={stage}&format=final" };
    }

    private void QueueCapture(int salePurchaseId, string stage)
    {
        var userId = _current.UserId;
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            try { await scope.ServiceProvider.GetRequiredService<ICameraCaptureService>()
                .CaptureForSalePurchaseAsync(salePurchaseId, stage, userId); }
            catch { /* capture must not affect committed weighment */ }
        });
    }

    private bool Duplicate(string? idempotencyKey, out IActionResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return false;
        var key = $"sale-purchase-idempotency:{_current.UserId}:{idempotencyKey}";
        if (_cache.TryGetValue(key, out _))
        {
            result = Conflict(new { message = "Duplicate SalePurchase request detected." });
            return true;
        }
        _cache.Set(key, true, TimeSpan.FromMinutes(10));
        return false;
    }
}
