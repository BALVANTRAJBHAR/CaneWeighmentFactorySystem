using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using CaneFactory.Application.Common;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

public record WeighmentCorrectionRequest(int VehicleTypeId, string VehicleNumber, string Revision,
    int? VarietyTypeId = null, int? VarietyId = null, decimal? ExpectedRate = null);

/// <summary>Administrative corrections only: never changes measured weights or payment records.</summary>
[ApiController]
[Authorize(Roles = "Admin,Developer")]
[Route("api/weighment-corrections")]
public class WeighmentCorrectionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAuditService _audit;
    public WeighmentCorrectionsController(AppDbContext db, ICurrentUser current, IAuditService audit)
    { _db = db; _current = current; _audit = audit; }

    // Purchase.Edit is also granted to operators; it must NOT grant access to this form/API.
    private IActionResult? Deny() => _current.UserId.HasValue &&
        (_current.Role ?? "").Split(',').Any(r => r.Trim() is "Admin" or "Developer")
        ? null : StatusCode(403, new { message = "Only Admin and Developer can correct weighments." });

    private IActionResult Paid() => Conflict(new { code = "PAYMENT_COMPLETED",
        message = "Payment already completed for this purchase. It cannot be edited." });

    private async Task<bool> IsPaid(Purchase p) => p.PaymentFlag == "Y" || p.PaymentStatus == "PAID" ||
        await _db.PaymentPurchases.AnyAsync(x => x.PurchaseId == p.Id && x.Payment.PaymentStatus == "COMPLETED");

    private static object Snapshot(Purchase p) => new { p.Id, p.VehicleTypeId, p.VehicleNumber,
        p.VarietyTypeId, p.VarietyId, p.GrossWeightQuintal, p.TareWeightQuintal, p.FinalWeightQuintal,
        p.Rate, amount = p.PurchaseAmount, p.GrossTareStatus, p.PaymentStatus, p.PaymentFlag,
        p.LockStatus, updatedAt = Utc(p.UpdatedAt), p.UpdatedBy };
    private static object Snapshot(SalePurchase p) => new { p.Id, p.VehicleTypeId, p.VehicleNumber,
        p.ItemId, p.PartyId, p.GrossWeightQuintal, p.TareWeightQuintal, p.FinalWeightQuintal,
        p.Rate, p.Amount, p.WeighmentStatus, updatedAt = Utc(p.UpdatedAt), p.UpdatedBy };
    // SQL Server stores UTC instants without DateTime.Kind; canonicalize before hashing/display.
    private static DateTime? Utc(DateTime? value) => value.HasValue
        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
    private static string Revision(object snapshot) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));

    [HttpGet("{kind}/{id:int}")]
    public async Task<IActionResult> Get(string kind, int id)
    {
        if (Deny() is { } denied) return denied;
        object record;
        int? updatedBy;
        if (kind == "purchase")
        {
            var p = await _db.Purchases.AsNoTracking().Include(x => x.Grower).Include(x => x.Variety)
                .Include(x => x.VehicleType).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (p == null) return NotFound(new { message = "Purchase ID not found." });
            if (await IsPaid(p)) return Paid();
            if (!p.Status || p.GrossTareStatus == "CANCELLED" || p.LockStatus == "LOCKED")
                return Conflict(new { message = "Inactive, cancelled or locked purchases cannot be corrected." });
            updatedBy = p.UpdatedBy;
            record = new { p.Id, p.GrowerCode, name = p.Grower.GrowerName, p.VarietyTypeId, p.VarietyId,
                varietyName = p.Variety.VarietyName, p.VehicleTypeId, p.VehicleType.VehicleTypeName, p.VehicleNumber,
                p.GrossWeightQuintal, p.TareWeightQuintal, p.FinalWeightQuintal, p.Rate,
                amount = p.PurchaseAmount, status = p.GrossTareStatus, p.PaymentStatus,
                rateDate = p.GrossDateTime.Date, updatedAt = Utc(p.UpdatedAt), p.UpdatedBy, revision = Revision(Snapshot(p)) };
        }
        else if (kind == "sale")
        {
            var p = await _db.SalePurchases.AsNoTracking().Include(x => x.Party).Include(x => x.Item)
                .Include(x => x.VehicleType).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (p == null) return NotFound(new { message = "Sale / Purchase ID not found." });
            if (!p.Status || p.WeighmentStatus == "CANCELLED")
                return Conflict(new { message = "Inactive or cancelled sale weighments cannot be corrected." });
            updatedBy = p.UpdatedBy;
            record = new { p.Id, name = p.Party.PartyName, p.Item.ItemName, p.VehicleTypeId,
                p.VehicleType.VehicleTypeName, p.VehicleNumber, p.GrossWeightQuintal, p.TareWeightQuintal,
                p.FinalWeightQuintal, p.Rate, p.Amount, status = p.WeighmentStatus, updatedAt = Utc(p.UpdatedAt), p.UpdatedBy,
                revision = Revision(Snapshot(p)) };
        }
        else return BadRequest(new { message = "Select purchase or sale." });

        var updater = updatedBy.HasValue
            ? await _db.Users.Where(x => x.Id == updatedBy).Select(x => x.Username).FirstOrDefaultAsync() : null;
        return Ok(new { record, updatedByName = updater,
            vehicleTypes = await _db.VehicleTypes.AsNoTracking().Where(x => !x.IsDeleted && x.Status)
                .OrderBy(x => x.VehicleTypeName).Select(x => new { x.Id, name = x.VehicleTypeName }).ToListAsync(),
            varietyTypes = await _db.VarietyTypes.AsNoTracking().Where(x => !x.IsDeleted && x.Status)
                .OrderBy(x => x.VarietyTypeName).Select(x => new { x.Id, name = x.VarietyTypeName }).ToListAsync(),
            varieties = await _db.Varieties.AsNoTracking().Where(x => !x.IsDeleted && x.Status)
                .OrderBy(x => x.VarietyName).Select(x => new { x.Id, name = x.VarietyName, x.VarietyTypeId }).ToListAsync() });
    }

    [HttpPost("{kind}/{id:int}/preview")]
    public Task<IActionResult> Preview(string kind, int id, WeighmentCorrectionRequest req) => Correct(kind, id, req, false);

    [HttpPut("{kind}/{id:int}")]
    public Task<IActionResult> Update(string kind, int id, WeighmentCorrectionRequest req) => Correct(kind, id, req, true);

    private async Task<IActionResult> Correct(string kind, int id, WeighmentCorrectionRequest req, bool save)
    {
        if (Deny() is { } denied) return denied;
        if (kind is not ("purchase" or "sale")) return BadRequest(new { message = "Select purchase or sale." });
        var vehicle = Validators.NormalizeVehicleNumber(req.VehicleNumber);
        if (vehicle.Length is < 4 or > 15 || vehicle.Any(c => !char.IsAsciiLetterOrDigit(c)))
            return BadRequest(new { message = "Enter a valid vehicle number (4-15 letters/digits)." });
        IActionResult result = Conflict(new { message = "Correction could not be completed." });
        try
        {
            await _db.ExecuteInTransactionAsync(async () =>
            {
                // Execution strategy retries must reload committed values, not a failed attempt's tracked state.
                _db.ChangeTracker.Clear();
                if (!await _db.VehicleTypes.AnyAsync(x => x.Id == req.VehicleTypeId && x.Status && !x.IsDeleted))
                { result = BadRequest(new { message = "Select an active vehicle type." }); return; }

                if (kind == "purchase")
                {
                    var p = await _db.Purchases.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                    if (p == null) { result = NotFound(new { message = "Purchase ID not found." }); return; }
                    // Must be rechecked INSIDE the update transaction, not just when the form is opened.
                    if (await IsPaid(p)) { result = Paid(); return; }
                    if (!p.Status || p.LockStatus == "LOCKED" || p.GrossTareStatus is not ("GROSS_DONE" or "TARE_DONE"))
                    { result = Conflict(new { message = "Inactive, cancelled or locked purchases cannot be corrected." }); return; }
                    var before = Snapshot(p);
                    if (req.Revision != Revision(before)) { result = Stale(); return; }
                    if (!await _db.VarietyTypes.AnyAsync(x => x.Id == req.VarietyTypeId && x.Status && !x.IsDeleted) ||
                        !await _db.Varieties.AnyAsync(x => x.Id == req.VarietyId && x.VarietyTypeId == req.VarietyTypeId && x.Status && !x.IsDeleted))
                    { result = BadRequest(new { message = "Select an active variety belonging to the selected variety type." }); return; }
                    if (p.GrossTareStatus == "GROSS_DONE" && await DuplicateCane(id, vehicle))
                    { result = Conflict(new { message = "This vehicle already has another gross pending tare." }); return; }

                    var rate = p.Rate; // A vehicle-only correction must preserve the original price snapshot.
                    if (p.VarietyTypeId != req.VarietyTypeId)
                    {
                        var rateDate = p.GrossDateTime.Date;
                        var rateMaster = await _db.Rates.Where(x => x.VarietyTypeId == req.VarietyTypeId && x.Status && !x.IsDeleted &&
                            x.EffectiveFrom.Date <= rateDate && (x.EffectiveTo == null || x.EffectiveTo.Value.Date >= rateDate))
                            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id).FirstOrDefaultAsync();
                        if (rateMaster == null || rateMaster.Rate <= 0)
                        { result = Conflict(new { message = $"No valid rate for the selected variety type on {rateDate:dd-MM-yyyy}. Configure Rate Master first." }); return; }
                        rate = WeightCalculator.R2(rateMaster.Rate);
                    }
                    if (save && req.ExpectedRate != rate)
                    { result = Conflict(new { message = "Rate changed or was not previewed. Calculate / Preview again before updating." }); return; }
                    if (p.GrossTareStatus == "TARE_DONE" && p.FinalWeightQuintal == null)
                    { result = Conflict(new { message = "Final weight is missing. This record cannot be repriced." }); return; }
                    var amount = p.VarietyTypeId == req.VarietyTypeId ? p.PurchaseAmount :
                        p.FinalWeightQuintal.HasValue ? WeightCalculator.R2(p.FinalWeightQuintal.Value * rate) : (decimal?)null;
                    if (amount is < 0 or >= 1000000000000m)
                    { result = BadRequest(new { message = "Calculated amount is outside the supported range." }); return; }
                    if (save)
                    {
                        p.VehicleTypeId = req.VehicleTypeId; p.VehicleNumber = vehicle;
                        p.VarietyTypeId = req.VarietyTypeId!.Value; p.VarietyId = req.VarietyId!.Value;
                        p.Rate = rate; p.PurchaseAmount = amount;
                        p.UpdatedBy = _current.UserId; p.UpdatedAt = DateTime.UtcNow;
                        // Audit and correction share a transaction; neither may succeed alone.
                        await _audit.LogAsync("CorrectWeighment", "Purchase", "Purchase", id.ToString(), before, Snapshot(p));
                    }
                    result = Ok(new { message = save ? "Purchase corrected successfully." : "Calculation ready. Weights are unchanged.",
                        vehicleNumber = vehicle, rate, amount, p.FinalWeightQuintal, p.UpdatedAt, p.UpdatedBy,
                        updatedByName = _current.Username, revision = Revision(Snapshot(p)) });
                }
                else
                {
                    var p = await _db.SalePurchases.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                    if (p == null) { result = NotFound(new { message = "Sale / Purchase ID not found." }); return; }
                    if (!p.Status || p.WeighmentStatus is not ("TARE_PENDING_GROSS" or "COMPLETED"))
                    { result = Conflict(new { message = "Inactive or cancelled sale weighments cannot be corrected." }); return; }
                    var before = Snapshot(p);
                    if (req.Revision != Revision(before)) { result = Stale(); return; }
                    // Non-cane records use Item/Party and have no payment linkage or variety rate master.
                    if (req.VarietyTypeId.HasValue || req.VarietyId.HasValue)
                    { result = BadRequest(new { message = "Variety fields apply only to cane purchases." }); return; }
                    if (p.WeighmentStatus == "TARE_PENDING_GROSS" && await DuplicateSale(id, vehicle))
                    { result = Conflict(new { message = "This vehicle already has another tare pending gross." }); return; }
                    if (save)
                    {
                        p.VehicleTypeId = req.VehicleTypeId; p.VehicleNumber = vehicle;
                        p.UpdatedBy = _current.UserId; p.UpdatedAt = DateTime.UtcNow;
                        await _audit.LogAsync("CorrectWeighment", "SalePurchase", "SalePurchase", id.ToString(), before, Snapshot(p));
                    }
                    result = Ok(new { message = save ? "Sale weighment corrected successfully." : "Vehicle correction ready. Weight, rate and amount are unchanged.",
                        vehicleNumber = vehicle, p.Rate, p.Amount, p.FinalWeightQuintal, p.UpdatedAt, p.UpdatedBy,
                        updatedByName = _current.Username, revision = Revision(Snapshot(p)) });
                }
            }, IsolationLevel.Serializable);
        }
        catch (DbUpdateConcurrencyException) { return Stale(); }
        return result;
    }

    private IActionResult Stale() => Conflict(new { code = "STALE_RECORD",
        message = "This record was changed or paid by another user. Search again before editing." });

    // Normalize legacy records too; newer rows are already stored canonically and have unique pending indexes.
    private async Task<bool> DuplicateCane(int id, string vehicle) =>
        (await _db.Purchases.Where(x => x.Id != id && !x.IsDeleted && x.GrossTareStatus == "GROSS_DONE")
            .Select(x => x.VehicleNumber).ToListAsync()).Any(x => Validators.NormalizeVehicleNumber(x) == vehicle);
    private async Task<bool> DuplicateSale(int id, string vehicle) =>
        (await _db.SalePurchases.Where(x => x.Id != id && !x.IsDeleted && x.WeighmentStatus == "TARE_PENDING_GROSS")
            .Select(x => x.VehicleNumber).ToListAsync()).Any(x => Validators.NormalizeVehicleNumber(x) == vehicle);
}
