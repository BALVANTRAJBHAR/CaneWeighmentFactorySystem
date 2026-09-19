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
/// Unified Cane Weighment API - GROSS and TARE for the single weighment main form.
/// All business calculation (Quintal, 2 decimals, cutting/tax, rate snapshot) is server-side.
/// </summary>
[ApiController]
[Authorize]
[Route("api/weighment")]
public class WeighmentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    private readonly ISequenceGenerator _seq;
    private readonly IMemoryCache _cache;
    private readonly ICameraCaptureService _capture;
    private readonly ISmsService _sms;
    private readonly WeighingService _weighing;
    private readonly ILogger<WeighmentController> _log;

    public WeighmentController(AppDbContext db, IAuditService audit, ICurrentUser current,
        ISequenceGenerator seq, IMemoryCache cache, ICameraCaptureService capture, ISmsService sms,
        WeighingService weighing, ILogger<WeighmentController> log)
    {
        _db = db; _audit = audit; _current = current; _seq = seq; _cache = cache; _capture = capture;
        _sms = sms; _weighing = weighing; _log = log;
    }

    private IActionResult? Deny(string action) =>
        _current.HasPermission($"Weighment.{action}") ? null
            : StatusCode(403, new { message = $"You do not have 'Weighment.{action}' permission." });

    /// <summary>
    /// Evidence must be persisted before a PDF is rendered.  Background capture caused the
    /// auto-print request to win the race and generate an image-less slip.
    /// A camera failure never rolls back a saved weight, but it is returned and logged visibly.
    /// </summary>
    private async Task<List<CameraCaptureResult>> CaptureEvidenceAsync(int purchaseId, string stage)
    {
        try
        {
            return await _capture.CaptureForPurchaseAsync(purchaseId, stage, _current.UserId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Evidence capture service failed for purchase {PurchaseId} at {Stage}", purchaseId, stage);
            return new List<CameraCaptureResult> { new() { Error = "Evidence capture service failed. Check the server log and image-folder permission." } };
        }
    }

    // -------------------------------------------------------------- PENDING GROSS GRID
    [HttpGet("pending-tare")]
    public async Task<IActionResult> PendingTare()
    {
        if (Deny("View") is { } d) return d;
        var items = await _db.Purchases
            .Where(p => p.GrossTareStatus == "GROSS_DONE" && !p.IsDeleted)
            .OrderBy(p => p.GrossDateTime)
            .Select(p => new
            {
                purchaseId = p.Id, p.GrowerCode, GrowerName = p.Grower.GrowerName,
                FatherName = p.Grower.FatherName, VillageName = p.Grower.Village.VillageName,
                p.VehicleNumber, p.GrossWeightQuintal, p.GrossDateTime, p.GrossByUserName
            }).ToListAsync();
        return Ok(items);
    }

    // -------------------------------------------------------------- TARE LOOKUP BY PURCHASE ID
    [HttpGet("purchase/{purchaseId:int}/for-tare")]
    public async Task<IActionResult> ForTare(int purchaseId)
    {
        if (Deny("View") is { } d) return d;
        var p = await _db.Purchases.Include(x => x.Grower).ThenInclude(g => g.Village)
            .Include(x => x.Variety).Include(x => x.VehicleType)
            .FirstOrDefaultAsync(x => x.Id == purchaseId);
        if (p == null) return NotFound(new { message = $"Purchase ID {purchaseId} does not exist. Example: 15482" });
        if (p.GrossTareStatus == "CANCELLED") return Conflict(new { message = $"Purchase {purchaseId} is CANCELLED." });
        if (p.GrossTareStatus == "TARE_DONE") return Conflict(new { message = $"Tare is already completed for Purchase {purchaseId}." });
        if (p.LockStatus == "LOCKED") return Conflict(new { message = $"Purchase {purchaseId} is LOCKED." });
        return Ok(new
        {
            purchaseId = p.Id, p.GrowerCode, GrowerName = p.Grower.GrowerName, FatherName = p.Grower.FatherName,
            VillageName = p.Grower.Village.VillageName, p.VehicleNumber, VehicleTypeName = p.VehicleType.VehicleTypeName,
            VarietyName = p.Variety.VarietyName, p.Rate, p.GrossWeightQuintal, p.GrossDateTime, p.GrossByUserName,
            p.CuttingPercent, p.TaxPercent
        });
    }

    // -------------------------------------------------------------- GROSS SAVE
    [HttpPost("gross")]
    public async Task<IActionResult> Gross(GrossSaveRequest req)
    {
        if (Deny("Create") is { } d) return d;
        if (IsDuplicateRequest(req.IdempotencyKey, out var dup)) return dup!;

        var grower = await _db.Growers.Include(g => g.Village)
            .FirstOrDefaultAsync(g => g.GrowerCode == req.GrowerCode.Trim() && !g.IsDeleted);
        if (grower == null) return NotFound(new { message = $"No grower found with code '{req.GrowerCode}'. Example: 101/1" });
        if (!grower.Status) return Conflict(new { message = $"Grower '{grower.GrowerName}' is INACTIVE and cannot be used." });
        if (!await _db.VehicleTypes.AnyAsync(v => v.Id == req.VehicleTypeId && !v.IsDeleted && v.Status))
            return BadRequest(new { message = "Selected Vehicle Type does not exist or is inactive." });
        var vehicleNumber = Validators.NormalizeVehicleNumber(req.VehicleNumber);
        if (vehicleNumber.Length is < 4 or > 15)
            return BadRequest(new { message = "Vehicle Number must be 4-15 characters. Example: UP32AB1234" });
        var variety = await _db.Varieties.FirstOrDefaultAsync(v => v.Id == req.VarietyId && !v.IsDeleted && v.Status);
        if (variety == null) return BadRequest(new { message = "Selected Variety does not exist or is inactive." });
        if (variety.VarietyTypeId != req.VarietyTypeId)
            return BadRequest(new { message = "Selected Variety does not belong to the selected Variety Type." });
        if (req.CuttingPercent is < 0 or > 100) return BadRequest(new { message = "Cutting % must be between 0 and 100. Example: 2.00" });
        if (req.TaxPercent is < 0 or > 100) return BadRequest(new { message = "Tax % must be between 0 and 100." });
        if (!_weighing.TryGetUsableWeight(out var liveKg, out var liveWeightError))
            return Conflict(new { message = liveWeightError });

        var grossQuintal = WeightCalculator.KgToQuintal(liveKg);
        var minError = await MinimumWeightErrorAsync(grossQuintal, applyGross: true);
        if (minError != null) return Conflict(new { message = minError, soundEvent = "BELOW_MINIMUM" });

        var now = DateTime.UtcNow;
        var eligibilityError = await CanStartNewCaneGrossAsync(vehicleNumber, now);
        if (eligibilityError != null) return Conflict(new { message = eligibilityError, soundEvent = "WEIGHING_ACTIVE" });

        var season = await _db.Seasons.FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted);
        if (season == null) return Conflict(new { message = "No ACTIVE season is configured. Ask Admin/Developer to activate a Season." });

        // Rate snapshot at gross time
        // Rate periods are configured as calendar dates, not instants. Comparing a local
        // date selected in the desktop UI with UTC time made a same-day rate unavailable
        // until its accidentally persisted clock time had passed.
        var rateDate = now.Date;
        var rate = await _db.Rates.Where(r => r.VarietyTypeId == req.VarietyTypeId && !r.IsDeleted && r.Status
                && r.EffectiveFrom.Date <= rateDate
                && (r.EffectiveTo == null || r.EffectiveTo.Value.Date >= rateDate))
            .OrderByDescending(r => r.EffectiveFrom).FirstOrDefaultAsync();
        if (rate == null)
            return Conflict(new { message = "No active Rate exists for this Variety Type. Configure Rate Master first." });

        Purchase? purchase = null;
        await _db.ExecuteInTransactionAsync(async () =>
        {
            var purchaseId = (int)await _seq.NextAsync("PurchaseId", 1);
            purchase = new Purchase
            {
                Id = purchaseId,
            GrowerId = grower.Id,
            VillageId = grower.VillageId,
            GrowerCode = grower.GrowerCode,
            VehicleTypeId = req.VehicleTypeId,
            VehicleNumber = vehicleNumber,
            VarietyTypeId = req.VarietyTypeId,
            VarietyId = req.VarietyId,
            ScaleReadingGrossKg = WeightCalculator.R2(liveKg),
            GrossWeightQuintal = grossQuintal,
            GrossDateTime = now,
            GrossByUserId = _current.UserId!.Value,
            GrossByUserName = _current.Username ?? "",
            CuttingPercent = WeightCalculator.R2(req.CuttingPercent),
            TaxPercent = WeightCalculator.R2(req.TaxPercent),
            Rate = rate.Rate,
            SeasonId = season.Id,
            GrossTareStatus = "GROSS_DONE",
            PaymentStatus = "NOT_ELIGIBLE",
                CreatedBy = _current.UserId
            };
            _db.Purchases.Add(purchase);
            await _db.SaveChangesAsync();
            await MarkPlatformClearRequiredAsync();
            await _db.SaveChangesAsync();
        });

        var purchaseId = purchase!.Id;

        await _audit.LogAsync("GrossWeighment", "Weighment", "Purchase", purchaseId.ToString(),
            newValue: new { purchase.GrowerCode, purchase.GrossWeightQuintal, purchase.VehicleNumber, purchase.Rate });
        var captureResults = await CaptureEvidenceAsync(purchaseId, "GROSS");
        return Ok(new
        {
            message = $"Gross weighment completed successfully. Purchase ID: {purchaseId}. Gross Weight: {grossQuintal:F2} Quintal.",
            purchaseId,
            grossWeightQuintal = grossQuintal,
            rate = rate.Rate,
            soundEvent = "WEIGHMENT_COMPLETED",
            autoPrint = await AutoPrintAsync("Gross", purchaseId),
            capture = new { requested = captureResults.Count, saved = captureResults.Count(x => x.Success), results = captureResults }
        });
    }

    // -------------------------------------------------------------- TARE SAVE
    [HttpPost("tare")]
    public async Task<IActionResult> Tare(TareSaveRequest req)
    {
        if (Deny("Edit") is { } d) return d;
        if (IsDuplicateRequest(req.IdempotencyKey, out var dup)) return dup!;

        var p = await _db.Purchases.Include(x => x.Grower).FirstOrDefaultAsync(x => x.Id == req.PurchaseId);
        if (p == null) return NotFound(new { message = $"Purchase ID {req.PurchaseId} does not exist." });
        if (p.GrossTareStatus == "CANCELLED") return Conflict(new { message = "This purchase is CANCELLED." });
        if (p.GrossTareStatus == "TARE_DONE") return Conflict(new { message = "Tare is already completed for this purchase (duplicate save blocked)." });
        if (p.LockStatus == "LOCKED") return Conflict(new { message = "This purchase is LOCKED." });
        if (!_weighing.TryGetUsableWeight(out var liveKg, out var liveWeightError))
            return Conflict(new { message = liveWeightError });

        var tareQuintal = WeightCalculator.KgToQuintal(liveKg);
        var minError = await MinimumWeightErrorAsync(tareQuintal, applyGross: false);
        if (minError != null) return Conflict(new { message = minError, soundEvent = "BELOW_MINIMUM" });
        if (tareQuintal >= p.GrossWeightQuintal)
            return Conflict(new { message = $"Tare weight ({tareQuintal:F2} Qtl) must be LESS than Gross weight ({p.GrossWeightQuintal:F2} Qtl)." });

        var (net, cutting, tax, final, amount) = WeightCalculator.Calculate(
            p.GrossWeightQuintal, tareQuintal, p.CuttingPercent, p.TaxPercent, p.Rate);

        p.ScaleReadingTareKg = WeightCalculator.R2(liveKg);
        p.TareWeightQuintal = tareQuintal;
        p.TareDateTime = DateTime.UtcNow;
        p.TareByUserId = _current.UserId;
        p.TareByUserName = _current.Username;
        p.NetWeightQuintal = net;
        p.CuttingWeightQuintal = cutting;
        p.TaxWeightQuintal = tax;
        p.FinalWeightQuintal = final;
        p.PurchaseAmount = amount;
        p.GrossTareStatus = "TARE_DONE";
        p.PaymentStatus = "PENDING";
        p.PaymentFlag = "N";
        p.UpdatedAt = DateTime.UtcNow;
        p.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await MarkPlatformClearRequiredAsync();
        await _db.SaveChangesAsync();

        await _audit.LogAsync("TareWeighment", "Weighment", "Purchase", p.Id.ToString(),
            newValue: new { p.TareWeightQuintal, p.NetWeightQuintal, p.FinalWeightQuintal, p.PurchaseAmount });
        var captureResults = await CaptureEvidenceAsync(p.Id, "TARE");

        var smsQueued = false;
        try
        {
            var placeholders = new Dictionary<string, string>
            {
                ["GrowerName"] = p.Grower.GrowerName,
                ["GrowerCode"] = p.GrowerCode,
                ["VehicleNumber"] = p.VehicleNumber,
                ["FinalWeight"] = final.ToString("F2"),
                ["PurchaseAmount"] = amount.ToString("F2")
            };
            var growerQueued = await _sms.QueueAsync("TARE_COMPLETED", p.GrowerId, p.Grower.Mobile, $"PUR-{p.Id}", placeholders);
            var recipients = await _db.SmsRecipients.AsNoTracking()
                .Where(x => !x.IsDeleted && x.Status && x.ReceiveCanePurchase).ToListAsync();
            var ownerQueued = new List<bool>();
            foreach (var recipient in recipients)
                ownerQueued.Add(await _sms.QueueForOperationalRecipientAsync("TARE_COMPLETED", recipient.MobileNumber,
                    $"PUR-{p.Id}-OWNER-{recipient.Id}", placeholders));
            smsQueued = growerQueued || ownerQueued.Any(x => x);
        }
        catch { /* SMS is a notification only - never affects a successful weighment */ }

        return Ok(new
        {
            message = $"Tare completed successfully. Final Weight: {final:F2} Quintal.",
            purchaseId = p.Id,
            tareWeightQuintal = tareQuintal,
            netWeightQuintal = net,
            cuttingWeightQuintal = cutting,
            taxWeightQuintal = tax,
            finalWeightQuintal = final,
            purchaseAmount = amount,
            soundEvent = "WEIGHMENT_COMPLETED",
            autoPrint = await AutoPrintAsync("Tare", p.Id),
            capture = new { requested = captureResults.Count, saved = captureResults.Count(x => x.Success), results = captureResults },
            smsQueued
        });
    }

    // -------------------------------------------------------------- WEIGHT RULES (client display)
    [HttpGet("rules")]
    public async Task<IActionResult> Rules()
    {
        if (Deny("View") is { } d) return d;
        var r = await _db.WeightRules.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted);
        var sound = await _db.SoundConfigs.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted);
        var messages = await _db.SoundMessages.AsNoTracking().Where(m => !m.IsDeleted && m.Enabled).ToListAsync();
        return Ok(new { weightRules = r, soundConfig = sound, soundMessages = messages });
    }

    private async Task<string?> CanStartNewCaneGrossAsync(string vehicleNumber, DateTime now)
    {
        var platformLock = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == "WeighbridgePlatformClearRequired");
        if (platformLock?.Value == "1")
            return "The previous vehicle has not yet cleared the platform. Wait until the indicator returns to zero before starting a new gross weighment.";

        var pending = await _db.Purchases.AsNoTracking().FirstOrDefaultAsync(p =>
            !p.IsDeleted && p.VehicleNumber == vehicleNumber && p.GrossTareStatus == "GROSS_DONE");
        if (pending != null)
            return $"Vehicle {vehicleNumber} already has pending tare for Purchase ID {pending.Id}. Complete its tare/final weighment before another gross.";

        var rules = await _db.WeightRules.AsNoTracking().FirstOrDefaultAsync(r => !r.IsDeleted);
        var cooldown = Math.Max(0, rules?.VehicleReweighCooldownMinutes ?? 30);
        if (cooldown == 0) return null;
        var lastCompletedAt = await _db.Purchases.AsNoTracking()
            .Where(p => !p.IsDeleted && p.VehicleNumber == vehicleNumber && p.GrossTareStatus == "TARE_DONE" && p.TareDateTime != null)
            .OrderByDescending(p => p.TareDateTime).Select(p => p.TareDateTime).FirstOrDefaultAsync();
        if (lastCompletedAt is not DateTime completedAt) return null;
        var releaseAt = completedAt.AddMinutes(cooldown);
        if (now < releaseAt)
        {
            var remaining = Math.Max(1, (int)Math.Ceiling((releaseAt - now).TotalMinutes));
            return $"Vehicle {vehicleNumber} was finalized at {completedAt.ToLocalTime():dd-MMM-yyyy HH:mm}. Reweigh is allowed after {cooldown} minutes; wait about {remaining} more minute(s).";
        }
        return null;
    }

    private async Task MarkPlatformClearRequiredAsync()
    {
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "WeighbridgePlatformClearRequired");
        if (setting == null)
        {
            setting = new CaneFactory.Domain.Entities.SystemSetting { Key = "WeighbridgePlatformClearRequired", Value = "1" };
            _db.SystemSettings.Add(setting);
        }
        else setting.Value = "1";
    }

    private async Task<string?> MinimumWeightErrorAsync(decimal weightQuintal, bool applyGross)
    {
        var rule = await _db.WeightRules.AsNoTracking().FirstOrDefaultAsync(r => !r.IsDeleted && r.Enabled);
        if (rule == null || !rule.ApplyToCanePurchase) return null;
        if (applyGross && !rule.ApplyToGross) return null;
        if (!applyGross && !rule.ApplyToTare) return null;
        return weightQuintal < rule.MinimumWeightQuintal
            ? $"Weight {weightQuintal:F2} Qtl is below the configured minimum {rule.MinimumWeightQuintal:F2} Qtl. Please position the vehicle on the platform."
            : null;
    }

    private async Task<object?> AutoPrintAsync(string stage, int purchaseId)
    {
        var cfg = await _db.PrintConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        if (cfg == null) return null;
        var copies = stage == "Gross" ? cfg.GrossCopies : cfg.TareCopies;
        return new
        {
            printerType = cfg.PrinterType,
            printerName = cfg.PrinterName,
            copies,
            language = cfg.Language,
            stage,
            shouldAutoPrint = cfg.AutoPrint && copies > 0,
            // Dot-matrix final output is ESC/P bytes, not a PDF. When physical
            // auto-print is off the client must receive an actual A4 PDF to open.
            documentUrl = $"/api/print/purchase/{purchaseId}?stage={stage.ToUpperInvariant()}&format=final" +
                (cfg.AutoPrint && copies > 0 ? "" : "&target=A4")
        };
    }

    private bool IsDuplicateRequest(string? key, out IActionResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (_cache.TryGetValue($"idem:{key}", out _))
        {
            result = Conflict(new { message = "Duplicate request detected (idempotency key already processed)." });
            return true;
        }
        _cache.Set($"idem:{key}", true, TimeSpan.FromMinutes(10));
        return false;
    }
}


