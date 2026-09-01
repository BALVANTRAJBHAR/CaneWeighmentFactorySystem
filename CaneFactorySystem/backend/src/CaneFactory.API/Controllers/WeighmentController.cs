using CaneFactory.Application.Common;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly IServiceScopeFactory _scopeFactory;

    public WeighmentController(AppDbContext db, IAuditService audit, ICurrentUser current,
        ISequenceGenerator seq, IMemoryCache cache, IServiceScopeFactory scopeFactory)
    {
        _db = db; _audit = audit; _current = current; _seq = seq; _cache = cache; _scopeFactory = scopeFactory;
    }

    private IActionResult? Deny(string action) =>
        _current.HasPermission($"Weighment.{action}") ? null
            : StatusCode(403, new { message = $"You do not have 'Weighment.{action}' permission." });

    /// <summary>Fire-and-forget capture on a fresh DI scope - never blocks or fails the weighment response.
    /// Per-camera failures are already audited inside ICameraCaptureService.</summary>
    private void QueueCapture(int purchaseId, string stage)
    {
        var userId = _current.UserId;
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ICameraCaptureService>();
            try { await svc.CaptureForPurchaseAsync(purchaseId, stage, userId); }
            catch { /* best-effort */ }
        });
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
        var vehicleNumber = Validators.NormUpper(req.VehicleNumber);
        if (vehicleNumber.Length is < 4 or > 15)
            return BadRequest(new { message = "Vehicle Number must be 4-15 characters. Example: UP32AB1234" });
        var variety = await _db.Varieties.FirstOrDefaultAsync(v => v.Id == req.VarietyId && !v.IsDeleted && v.Status);
        if (variety == null) return BadRequest(new { message = "Selected Variety does not exist or is inactive." });
        if (variety.VarietyTypeId != req.VarietyTypeId)
            return BadRequest(new { message = "Selected Variety does not belong to the selected Variety Type." });
        if (req.CuttingPercent is < 0 or > 100) return BadRequest(new { message = "Cutting % must be between 0 and 100. Example: 2.00" });
        if (req.TaxPercent is < 0 or > 100) return BadRequest(new { message = "Tax % must be between 0 and 100." });
        if (req.ScaleReadingKg <= 0) return BadRequest(new { message = "Live weight is not valid. Check the weighing indicator." });

        var grossQuintal = WeightCalculator.KgToQuintal(req.ScaleReadingKg);
        var minError = await MinimumWeightErrorAsync(grossQuintal, applyGross: true);
        if (minError != null) return Conflict(new { message = minError, soundEvent = "BELOW_MINIMUM" });

        var season = await _db.Seasons.FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted);
        if (season == null) return Conflict(new { message = "No ACTIVE season is configured. Ask Admin/Developer to activate a Season." });

        // Rate snapshot at gross time
        var now = DateTime.UtcNow;
        var rate = await _db.Rates.Where(r => r.VarietyTypeId == req.VarietyTypeId && !r.IsDeleted && r.Status
                && r.EffectiveFrom <= now && (r.EffectiveTo == null || r.EffectiveTo >= now))
            .OrderByDescending(r => r.EffectiveFrom).FirstOrDefaultAsync();
        if (rate == null)
            return Conflict(new { message = "No active Rate exists for this Variety Type. Configure Rate Master first." });

        await using var tx = await _db.Database.BeginTransactionAsync();
        var purchaseId = (int)await _seq.NextAsync("PurchaseId", 1);
        var purchase = new Purchase
        {
            Id = purchaseId,
            GrowerId = grower.Id,
            VillageId = grower.VillageId,
            GrowerCode = grower.GrowerCode,
            VehicleTypeId = req.VehicleTypeId,
            VehicleNumber = vehicleNumber,
            VarietyTypeId = req.VarietyTypeId,
            VarietyId = req.VarietyId,
            ScaleReadingGrossKg = WeightCalculator.R2(req.ScaleReadingKg),
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
        await tx.CommitAsync();

        await _audit.LogAsync("GrossWeighment", "Weighment", "Purchase", purchaseId.ToString(),
            newValue: new { purchase.GrowerCode, purchase.GrossWeightQuintal, purchase.VehicleNumber, purchase.Rate });
        QueueCapture(purchaseId, "GROSS");
        return Ok(new
        {
            message = $"Gross weighment completed successfully. Purchase ID: {purchaseId}. Gross Weight: {grossQuintal:F2} Quintal.",
            purchaseId,
            grossWeightQuintal = grossQuintal,
            rate = rate.Rate,
            soundEvent = "WEIGHMENT_COMPLETED",
            autoPrint = await AutoPrintAsync("Gross"),
            captureQueued = true
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
        if (req.ScaleReadingKg <= 0) return BadRequest(new { message = "Live weight is not valid. Check the weighing indicator." });

        var tareQuintal = WeightCalculator.KgToQuintal(req.ScaleReadingKg);
        var minError = await MinimumWeightErrorAsync(tareQuintal, applyGross: false);
        if (minError != null) return Conflict(new { message = minError, soundEvent = "BELOW_MINIMUM" });
        if (tareQuintal >= p.GrossWeightQuintal)
            return Conflict(new { message = $"Tare weight ({tareQuintal:F2} Qtl) must be LESS than Gross weight ({p.GrossWeightQuintal:F2} Qtl)." });

        var (net, cutting, tax, final, amount) = WeightCalculator.Calculate(
            p.GrossWeightQuintal, tareQuintal, p.CuttingPercent, p.TaxPercent, p.Rate);

        p.ScaleReadingTareKg = WeightCalculator.R2(req.ScaleReadingKg);
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

        await _audit.LogAsync("TareWeighment", "Weighment", "Purchase", p.Id.ToString(),
            newValue: new { p.TareWeightQuintal, p.NetWeightQuintal, p.FinalWeightQuintal, p.PurchaseAmount });
        QueueCapture(p.Id, "TARE");
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
            autoPrint = await AutoPrintAsync("Tare"),
            captureQueued = true,
            smsQueued = false // SMS gateway integration activates in Phase 10
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

    private async Task<object?> AutoPrintAsync(string stage)
    {
        var cfg = await _db.PrintConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        if (cfg == null || !cfg.AutoPrint) return null;
        return new
        {
            printerType = cfg.PrinterType,
            printerName = cfg.PrinterName,
            copies = stage == "Gross" ? cfg.GrossCopies : cfg.TareCopies,
            language = cfg.Language
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
