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
using Microsoft.Extensions.Logging;

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
    private readonly ICameraCaptureService _capture;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SalePurchaseWeighmentController> _log;

    public SalePurchaseWeighmentController(AppDbContext db, ICurrentUser current, IAuditService audit,
        ISequenceGenerator sequence, WeighingService weighing, ISmsService sms, ICameraCaptureService capture, IMemoryCache cache,
        ILogger<SalePurchaseWeighmentController> log)
    {
        _db = db; _current = current; _audit = audit; _sequence = sequence; _weighing = weighing;
        _sms = sms; _capture = capture; _cache = cache; _log = log;
    }

    private IActionResult? Deny(string action) => _current.HasPermission($"SalePurchase.{action}")
        ? null : StatusCode(403, new { message = $"You do not have 'SalePurchase.{action}' permission." });

    [HttpGet("{id:int}/images")]
    public async Task<IActionResult> Images(int id)
    {
        if (Deny("View") is { } denied) return denied;
        if (!await _db.SalePurchases.AnyAsync(x => x.Id == id && !x.IsDeleted))
            return NotFound(new { message = "SalePurchase not found." });
        var images = await _db.SalePurchaseImages.AsNoTracking()
            .Where(x => x.SalePurchaseId == id && x.Status)
            .OrderBy(x => x.CaptureStage).ThenBy(x => x.ImageName)
            .Select(x => new { x.Id, x.CameraId, x.CaptureStage, x.ImageName, x.FileHash, x.CapturedAt, x.CapturedBy })
            .ToListAsync();
        return Ok(images);
    }

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
        if (await MinimumWeightErrorAsync(tare, applyGross: false) is { } tareError)
            return Conflict(new { message = tareError });
        var now = DateTime.UtcNow;
        SalePurchase? record = null;
        try
        {
            await _db.ExecuteInTransactionAsync(async () =>
            {
            var id = (int)await _sequence.NextAsync("SalePurchaseId", 1);
            record = new SalePurchase
            {
                Id = id, ItemId = req.ItemId, PartyId = req.PartyId, VehicleTypeId = req.VehicleTypeId,
            VehicleNumber = vehicle, DriverName = driver, Remark = Validators.Norm(req.Remark),
            ScaleReadingTareKg = WeightCalculator.R2(liveKg), TareWeightQuintal = tare,
            TareDateTime = now, TareByUserId = _current.UserId!.Value, TareByUserName = _current.Username ?? "",
                WeighmentStatus = "TARE_PENDING_GROSS", CreatedBy = _current.UserId
            };
            _db.SalePurchases.Add(record);
            await _db.SaveChangesAsync();
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SalePurchase tare save failed for item {ItemId}, party {PartyId}", req.ItemId, req.PartyId);
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "SalePurchase tare could not be saved. Please contact the administrator.");
        }
        var savedRecord = record!;
        try { await _audit.LogAsync("TareSave", "SalePurchase", "SalePurchase", savedRecord.Id.ToString(), newValue: new
        { savedRecord.ItemId, savedRecord.PartyId, savedRecord.VehicleNumber, savedRecord.TareWeightQuintal, savedRecord.TareDateTime }); }
        catch (Exception ex) { _log.LogError(ex, "SalePurchase tare {SalePurchaseId} audit failed after commit", savedRecord.Id); }
        var captureResults = await CaptureEvidenceAsync(savedRecord.Id, "TARE");
        return Ok(new { message = $"Tare saved. SalePurchase ID: {savedRecord.Id}. Pending gross weighment.", salePurchaseId = savedRecord.Id,
            tareWeightQuintal = tare, status = savedRecord.WeighmentStatus, autoPrint = await AutoPrintAsync(savedRecord.Id, "TARE"),
            capture = new { requested = captureResults.Count, saved = captureResults.Count(x => x.Success), results = captureResults } });
    }

    [HttpPost("gross")]
    public async Task<IActionResult> SaveGross(SalePurchaseGrossSaveRequest req)
    {
        if (Deny("Edit") is { } denied) return denied;
        if (Duplicate(req.IdempotencyKey, out var duplicate)) return duplicate!;
        if (!_weighing.TryGetUsableWeight(out var liveKg, out var deviceError))
            return Conflict(new { message = deviceError });
        if (req.Rate is < 0) return BadRequest(new { message = "Rate cannot be negative." });

        SalePurchase? record = null;
        decimal gross = 0;
        decimal finalWeight = 0;
        try
        {
            // The complete state-check and update run inside the configured retry strategy.
            // Serializable still prevents two operators completing the same pending tare.
            await _db.ExecuteInTransactionAsync(async () =>
            {
                record = await _db.SalePurchases.FirstOrDefaultAsync(x => x.Id == req.SalePurchaseId && !x.IsDeleted);
                if (record == null) throw new SalePurchaseStateException(404, $"SalePurchase ID {req.SalePurchaseId} does not exist.");
                if (record.WeighmentStatus == "CANCELLED") throw new SalePurchaseStateException(409, "Cancelled SalePurchase cannot be processed.");
                if (record.WeighmentStatus != "TARE_PENDING_GROSS") throw new SalePurchaseStateException(409, "Gross is already completed for this SalePurchase.");

                gross = WeightCalculator.KgToQuintal(liveKg);
                var minimumError = await MinimumWeightErrorAsync(gross, applyGross: true);
                if (minimumError != null) throw new SalePurchaseStateException(409, minimumError);
                finalWeight = WeightCalculator.R2(gross - record.TareWeightQuintal);
                if (finalWeight < 0) throw new SalePurchaseStateException(409, "Gross weight cannot be less than tare weight.");
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
            }, System.Data.IsolationLevel.Serializable);
        }
        catch (SalePurchaseStateException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SalePurchase gross save failed for SalePurchase {SalePurchaseId}", req.SalePurchaseId);
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "SalePurchase gross could not be saved. Please contact the administrator.");
        }
        var completedRecord = record ?? throw new InvalidOperationException("SalePurchase completion did not return a record.");
        try { await _audit.LogAsync("GrossSave", "SalePurchase", "SalePurchase", completedRecord.Id.ToString(), newValue: new
        { completedRecord.GrossWeightQuintal, completedRecord.TareWeightQuintal, completedRecord.FinalWeightQuintal, completedRecord.Rate, completedRecord.Amount }); }
        catch (Exception ex) { _log.LogError(ex, "SalePurchase gross {SalePurchaseId} audit failed after commit", completedRecord.Id); }
        var captureResults = await CaptureEvidenceAsync(completedRecord.Id, "GROSS");
        var party = await _db.Parties.AsNoTracking().Where(x => x.Id == completedRecord.PartyId)
            .Select(x => new { x.PartyName, x.Mobile, x.Status, x.IsDeleted }).FirstAsync();
        var configuredRecipients = await _db.SmsConfigs.AsNoTracking().Where(x => !x.IsDeleted && x.Enabled)
            .Select(x => x.SalePurchaseRecipients).FirstOrDefaultAsync() ?? "";
        var recipients = configuredRecipients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length == 10 && x.All(char.IsDigit)).Distinct().ToList();
        var placeholders = new Dictionary<string, string>
            {
                ["SalePurchaseId"] = completedRecord.Id.ToString(), ["PartyName"] = party.PartyName, ["FinalWeight"] = finalWeight.ToString("F2"),
                ["Amount"] = completedRecord.Amount?.ToString("F2") ?? ""
            };
        // A Party is mandatory for the transaction; successful-event SMS is additionally gated by
        // explicit configured recipient numbers, never implicitly sent to an arbitrary party mobile.
        bool[] queued;
        try
        {
            queued = party.Status && !party.IsDeleted
                ? await Task.WhenAll(recipients.Select((mobile, index) => _sms.QueueForPartyAsync("SALE_PURCHASE_COMPLETED",
                    completedRecord.PartyId, mobile, $"SP-{completedRecord.Id}-{index}", placeholders)))
                : Array.Empty<bool>();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SalePurchase {SalePurchaseId} SMS queue failed after commit", completedRecord.Id);
            queued = Array.Empty<bool>();
        }
        var smsQueued = queued.Any(x => x);
        return Ok(new { message = $"SalePurchase {completedRecord.Id} completed successfully. Final Weight: {finalWeight:F2} Quintal.",
            salePurchaseId = completedRecord.Id, grossWeightQuintal = gross, finalWeightQuintal = finalWeight,
            amount = completedRecord.Amount, status = completedRecord.WeighmentStatus, smsQueued, autoPrint = await AutoPrintAsync(completedRecord.Id, "GROSS"),
            capture = new { requested = captureResults.Count, saved = captureResults.Count(x => x.Success), results = captureResults } });
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
        if (cfg == null) return null;
        return new { printerType = cfg.PrinterType, printerName = cfg.PrinterName, copies = cfg.SalePurchaseCopies,
            language = cfg.Language, stage, shouldAutoPrint = cfg.AutoPrint && cfg.SalePurchaseCopies > 0,
            documentUrl = $"/api/print/sale-purchase/{id}?stage={stage}&format=final" +
                (cfg.AutoPrint && cfg.SalePurchaseCopies > 0 ? "" : "&target=A4") };
    }

    private sealed class SalePurchaseStateException : Exception
    {
        public int StatusCode { get; }
        public SalePurchaseStateException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    }

    private async Task<string?> MinimumWeightErrorAsync(decimal weightQuintal, bool applyGross)
    {
        var rule = await _db.WeightRules.AsNoTracking().FirstOrDefaultAsync(r => !r.IsDeleted && r.Enabled);
        if (rule == null || !rule.ApplyToSalePurchase) return null;
        if (applyGross && !rule.ApplyToGross) return null;
        if (!applyGross && !rule.ApplyToTare) return null;
        return weightQuintal < rule.MinimumWeightQuintal
            ? $"Weight {weightQuintal:F2} Qtl is below the configured minimum {rule.MinimumWeightQuintal:F2} Qtl. Please position the vehicle on the platform."
            : null;
    }

    private async Task<List<CameraCaptureResult>> CaptureEvidenceAsync(int salePurchaseId, string stage)
    {
        try
        {
            return await _capture.CaptureForSalePurchaseAsync(salePurchaseId, stage, _current.UserId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Evidence capture service failed for SalePurchase {SalePurchaseId} at {Stage}", salePurchaseId, stage);
            return new List<CameraCaptureResult> { new() { Error = "Evidence capture service failed. Check the server log and image-folder permission." } };
        }
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
