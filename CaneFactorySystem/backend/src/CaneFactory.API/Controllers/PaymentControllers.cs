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
/// Phase 9: Payment + Advice. A Payment batches one or more eligible Purchases (TARE_DONE, unpaid,
/// unlocked) for a single Grower under ONE new continuous Advice Number, automatically settles as
/// much of the Grower's ACTIVE Loan outstanding as the payable amount allows (creating LoanRecovery
/// rows tagged with this PaymentId), then records the Net Payable via CASH/BANK/MOBILE_UPI. Payment
/// modes are a plain configurable master (PaymentModeMaster) - no gateway is ever called from here,
/// so a real payment provider can be wired in later without touching this controller.
/// </summary>
[ApiController]
[Authorize]
[Route("api/payments")]
public class PaymentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    private readonly ISequenceGenerator _seq;
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;

    public PaymentController(AppDbContext db, IAuditService audit, ICurrentUser current,
        ISequenceGenerator seq, IMemoryCache cache, IServiceScopeFactory scopeFactory)
    {
        _db = db; _audit = audit; _current = current; _seq = seq; _cache = cache; _scopeFactory = scopeFactory;
    }

    private IActionResult? Deny(string action) =>
        _current.HasPermission($"Payment.{action}") ? null
            : StatusCode(403, new { message = $"You do not have 'Payment.{action}' permission." });

    private bool IsFarmerOnly =>
        (_current.Role ?? "").Split(',').All(r => r is "Farmer" or "") && (_current.Role ?? "") != "";

    /// <summary>Farmer accounts see ONLY payments belonging to their own Grower record.</summary>
    private async Task<IQueryable<Payment>> ScopedQueryAsync()
    {
        var q = _db.Payments.Where(p => !p.IsDeleted);
        if (!IsFarmerOnly) return q;
        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == _current.UserId);
        return q.Where(p => p.Grower.Mobile == user.Mobile);
    }

    /// <summary>Fire-and-forget Cash Evidence capture on a fresh DI scope - never blocks or fails
    /// the payment response. Per-camera failures are already audited inside ICameraCaptureService.</summary>
    private void QueueCapture(int paymentId)
    {
        var userId = _current.UserId;
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ICameraCaptureService>();
            try { await svc.CaptureForPaymentAsync(paymentId, userId); }
            catch { /* best-effort */ }
        });
    }

    private async Task<object?> AutoPrintAsync(int paymentId)
    {
        var cfg = await _db.PrintConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        if (cfg == null || !cfg.AutoPrint || cfg.PaymentCopies <= 0) return null;
        return new
        {
            printerType = cfg.PrinterType,
            printerName = cfg.PrinterName,
            copies = cfg.PaymentCopies,
            language = cfg.Language,
            documentUrl = $"/api/print/payment/{paymentId}?format=final"
        };
    }

    /// <summary>Purchases eligible for payment for a Grower: TARE_DONE, PaymentStatus=PENDING, UNLOCKED.
    /// SINGLE narrows to one PurchaseId, DATE_RANGE narrows by TareDateTime, FARMER takes all of them.</summary>
    private IQueryable<Purchase> EligiblePurchasesQuery(string mode, int growerId, int? purchaseId, DateTime? from, DateTime? to)
    {
        var q = _db.Purchases.Where(p => p.GrowerId == growerId && !p.IsDeleted
            && p.GrossTareStatus == "TARE_DONE" && p.PaymentStatus == "PENDING" && p.LockStatus == "UNLOCKED");
        if (mode == "SINGLE") return q.Where(p => p.Id == purchaseId!.Value);
        if (mode == "DATE_RANGE")
        {
            if (from.HasValue) q = q.Where(p => p.TareDateTime >= from);
            if (to.HasValue) q = q.Where(p => p.TareDateTime <= to);
        }
        return q;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? growerCode, [FromQuery] string? status,
        [FromQuery] int? adviceNumber, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (Deny("View") is { } d) return d;
        var q = await ScopedQueryAsync();
        if (!string.IsNullOrWhiteSpace(growerCode)) q = q.Where(p => p.GrowerCode == growerCode.Trim());
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.PaymentStatus == status);
        if (adviceNumber.HasValue) q = q.Where(p => p.AdviceNumber == adviceNumber);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                paymentId = p.Id, p.AdviceNumber, p.GrowerCode, GrowerName = p.Grower.GrowerName,
                VillageName = p.Grower.Village.VillageName, p.TotalPurchaseAmount, p.LoanDeductedAmount,
                p.NetPayableAmount, PaymentModeName = p.PaymentMode.ModeName, p.TransactionRefNumber,
                p.PaymentDate, p.PaidByUserName, p.PaymentStatus, p.PrintCount
            }).ToListAsync();
        return Ok(new { items, totalCount = total, page, pageSize });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (Deny("View") is { } d) return d;
        var q = await ScopedQueryAsync();
        var p = await q.Where(x => x.Id == id).FirstOrDefaultAsync();
        // 404 (not 403) for out-of-scope IDs: does not leak other farmers' record existence
        if (p == null) return NotFound(new { message = "Payment not found." });
        await _db.Entry(p).Reference(x => x.Grower).LoadAsync();
        await _db.Entry(p.Grower).Reference(g => g.Village).LoadAsync();
        await _db.Entry(p).Reference(x => x.PaymentMode).LoadAsync();
        var purchaseIds = await _db.PaymentPurchases.Where(pp => pp.PaymentId == id)
            .OrderBy(pp => pp.PurchaseId).Select(pp => pp.PurchaseId).ToListAsync();
        return Ok(new
        {
            paymentId = p.Id, p.AdviceNumber, p.GrowerCode, GrowerName = p.Grower.GrowerName,
            FatherName = p.Grower.FatherName, VillageName = p.Grower.Village.VillageName,
            p.TotalPurchaseAmount, p.LoanDeductedAmount, p.NetPayableAmount,
            PaymentModeName = p.PaymentMode.ModeName, p.TransactionRefNumber, p.PaymentDate,
            p.PaidByUserName, p.PaymentStatus, p.CancelReason, p.PrintCount, purchaseIds
        });
    }

    /// <summary>Read-only preview BEFORE committing a payment - shows exactly which purchases,
    /// total amount, and estimated loan deduction/net-payable a POST with the same criteria would
    /// produce. Lets the Flutter UI show the operator a confirmation screen first.</summary>
    [HttpGet("eligible-purchases")]
    public async Task<IActionResult> EligiblePurchases([FromQuery] string selectionMode, [FromQuery] string growerCode,
        [FromQuery] int? purchaseId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        if (Deny("View") is { } d) return d;
        var mode = (selectionMode ?? "").Trim().ToUpperInvariant();
        if (mode is not ("SINGLE" or "DATE_RANGE" or "FARMER"))
            return BadRequest(new { message = "selectionMode must be SINGLE, DATE_RANGE or FARMER." });
        if (mode == "SINGLE" && !purchaseId.HasValue)
            return BadRequest(new { message = "purchaseId is required for SINGLE selection mode." });

        var grower = await _db.Growers.FirstOrDefaultAsync(g => g.GrowerCode == growerCode.Trim() && !g.IsDeleted);
        if (grower == null) return NotFound(new { message = $"No grower found with code '{growerCode}'. Example: 101/1" });

        var eligible = await EligiblePurchasesQuery(mode, grower.Id, purchaseId, fromDate, toDate)
            .OrderBy(p => p.GrossDateTime)
            .Select(p => new { purchaseId = p.Id, p.VehicleNumber, p.FinalWeightQuintal, p.Rate, p.PurchaseAmount, p.TareDateTime })
            .ToListAsync();
        var totalPurchaseAmount = WeightCalculator.R2(eligible.Sum(p => p.PurchaseAmount ?? 0));
        var outstandingLoans = await _db.Loans.Where(l => l.GrowerCode == grower.GrowerCode && l.LoanStatus == "ACTIVE" && !l.IsDeleted)
            .OrderBy(l => l.IssueDate)
            .Select(l => new { loanId = l.Id, l.OutstandingAmount }).ToListAsync();
        var estimatedDeduction = WeightCalculator.R2(Math.Min(outstandingLoans.Sum(l => l.OutstandingAmount), totalPurchaseAmount));
        return Ok(new
        {
            growerCode = grower.GrowerCode,
            eligiblePurchases = eligible,
            totalPurchaseAmount,
            outstandingLoans,
            totalOutstandingLoan = outstandingLoans.Sum(l => l.OutstandingAmount),
            estimatedLoanDeduction = estimatedDeduction,
            estimatedNetPayable = WeightCalculator.R2(totalPurchaseAmount - estimatedDeduction)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Issue(PaymentCreateRequest req)
    {
        if (Deny("Create") is { } d) return d;
        if (IsDuplicateRequest(req.IdempotencyKey, out var dup)) return dup!;

        var mode = (req.SelectionMode ?? "").Trim().ToUpperInvariant();
        if (mode is not ("SINGLE" or "DATE_RANGE" or "FARMER"))
            return BadRequest(new { message = "selectionMode must be SINGLE, DATE_RANGE or FARMER." });
        if (mode == "SINGLE" && !req.PurchaseId.HasValue)
            return BadRequest(new { message = "purchaseId is required for SINGLE selection mode." });

        var grower = await _db.Growers.Include(g => g.Village)
            .FirstOrDefaultAsync(g => g.GrowerCode == req.GrowerCode.Trim() && !g.IsDeleted);
        if (grower == null) return NotFound(new { message = $"No grower found with code '{req.GrowerCode}'. Example: 101/1" });

        var paymentMode = await _db.PaymentModes.FirstOrDefaultAsync(m => m.Id == req.PaymentModeId && !m.IsDeleted && m.Status);
        if (paymentMode == null) return BadRequest(new { message = "Selected Payment Mode does not exist or is inactive." });

        var eligible = await EligiblePurchasesQuery(mode, grower.Id, req.PurchaseId, req.FromDate, req.ToDate).ToListAsync();
        if (eligible.Count == 0) return Conflict(new { message = "No payable purchases found for the selected criteria." });

        var season = await _db.Seasons.FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted);
        if (season == null) return Conflict(new { message = "No ACTIVE season is configured. Ask Admin/Developer to activate a Season." });

        var totalPurchaseAmount = WeightCalculator.R2(eligible.Sum(p => p.PurchaseAmount ?? 0));

        await using var tx = await _db.Database.BeginTransactionAsync();
        var paymentId = (int)await _seq.NextAsync("PaymentId", 1);
        var adviceNumber = (int)await _seq.NextAsync("AdviceNumber", 1);

        // Automatic Loan deduction - FIFO (oldest ACTIVE loan first), never exceeding the payable amount.
        var remaining = totalPurchaseAmount;
        decimal totalDeducted = 0;
        var activeLoans = await _db.Loans.Where(l => l.GrowerCode == grower.GrowerCode && l.LoanStatus == "ACTIVE" && !l.IsDeleted)
            .OrderBy(l => l.IssueDate).ToListAsync();
        foreach (var loan in activeLoans)
        {
            if (remaining <= 0) break;
            var deduct = WeightCalculator.R2(Math.Min(loan.OutstandingAmount, remaining));
            if (deduct <= 0) continue;
            var lrId = (int)await _seq.NextAsync("LRId", 1);
            _db.LoanRecoveries.Add(new LoanRecovery
            {
                Id = lrId,
                LoanId = loan.Id,
                GrowerId = loan.GrowerId,
                GrowerCode = loan.GrowerCode,
                RecoveryAmount = deduct,
                RecoveryDate = DateTime.UtcNow,
                RecoveredByUserId = _current.UserId!.Value,
                RecoveredByUserName = _current.Username ?? "",
                Remarks = $"Auto-deducted during Payment {paymentId} (Advice {adviceNumber})",
                RecoveryStatus = "ACTIVE",
                PaymentId = paymentId,
                CreatedBy = _current.UserId
            });
            loan.RecoveredAmount = WeightCalculator.R2(loan.RecoveredAmount + deduct);
            loan.OutstandingAmount = WeightCalculator.R2(loan.OutstandingAmount - deduct);
            if (loan.OutstandingAmount == 0) loan.LoanStatus = "CLOSED";
            loan.UpdatedAt = DateTime.UtcNow;
            loan.UpdatedBy = _current.UserId;
            remaining -= deduct;
            totalDeducted += deduct;
        }
        totalDeducted = WeightCalculator.R2(totalDeducted);
        var netPayable = WeightCalculator.R2(totalPurchaseAmount - totalDeducted);

        var payment = new Payment
        {
            Id = paymentId,
            AdviceNumber = adviceNumber,
            GrowerId = grower.Id,
            GrowerCode = grower.GrowerCode,
            VillageId = grower.VillageId,
            TotalPurchaseAmount = totalPurchaseAmount,
            LoanDeductedAmount = totalDeducted,
            NetPayableAmount = netPayable,
            PaymentModeId = paymentMode.Id,
            TransactionRefNumber = req.TransactionRefNumber,
            PaymentDate = DateTime.UtcNow,
            PaidByUserId = _current.UserId!.Value,
            PaidByUserName = _current.Username ?? "",
            PaymentStatus = "COMPLETED",
            SeasonId = season.Id,
            CreatedBy = _current.UserId
        };
        _db.Payments.Add(payment);

        foreach (var p in eligible)
        {
            _db.PaymentPurchases.Add(new PaymentPurchase { PaymentId = paymentId, PurchaseId = p.Id, PurchaseAmountAtPayment = p.PurchaseAmount ?? 0 });
            p.PaymentStatus = "PAID";
            p.PaymentFlag = "Y";
            p.AdviceNumber = adviceNumber;
            p.UpdatedAt = DateTime.UtcNow;
            p.UpdatedBy = _current.UserId;
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        await _audit.LogAsync("Pay", "Payment", "Payment", paymentId.ToString(),
            newValue: new { payment.GrowerCode, payment.AdviceNumber, payment.TotalPurchaseAmount, payment.LoanDeductedAmount, payment.NetPayableAmount, purchaseCount = eligible.Count });

        var isCash = paymentMode.ModeCode == "CASH";
        if (isCash) QueueCapture(paymentId);

        return Ok(new
        {
            message = $"Payment completed. Payment ID: {paymentId}. Advice Number: {adviceNumber}. Net Payable: Rs {netPayable:F2}.",
            paymentId,
            adviceNumber,
            totalPurchaseAmount,
            loanDeductedAmount = totalDeducted,
            netPayableAmount = netPayable,
            purchaseCount = eligible.Count,
            autoPrint = await AutoPrintAsync(paymentId),
            captureQueued = isCash,
            smsQueued = false // SMS gateway integration activates in Phase 10
        });
    }

    /// <summary>Reverses a COMPLETED Payment: the Purchases it covered become payable again (new
    /// Advice Number on the next payment), and any LoanRecovery it auto-created is reversed (restoring
    /// Loan outstanding, reopening a CLOSED loan). The Payment record itself is never deleted - only
    /// its status flips to CANCELLED, keeping full history for audit.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] Dictionary<string, string> body)
    {
        if (Deny("Cancel") is { } d) return d;
        var reason = (body.GetValueOrDefault("reason") ?? "").Trim();
        if (reason.Length < 5) return BadRequest(new { message = "A cancellation reason (min 5 characters) is required." });

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
        if (payment == null) return NotFound(new { message = "Payment not found." });
        if (payment.PaymentStatus != "COMPLETED") return Conflict(new { message = $"Payment {id} is already {payment.PaymentStatus}." });

        var paymentPurchases = await _db.PaymentPurchases.Where(pp => pp.PaymentId == id).ToListAsync();
        foreach (var pp in paymentPurchases)
        {
            var purchase = await _db.Purchases.FirstOrDefaultAsync(p => p.Id == pp.PurchaseId);
            if (purchase == null) continue;
            purchase.PaymentStatus = "PENDING";
            purchase.PaymentFlag = "N";
            purchase.UpdatedAt = DateTime.UtcNow;
            purchase.UpdatedBy = _current.UserId;
        }

        var recoveries = await _db.LoanRecoveries.Where(r => r.PaymentId == id && r.RecoveryStatus == "ACTIVE").ToListAsync();
        foreach (var recovery in recoveries)
        {
            var loan = await _db.Loans.FirstOrDefaultAsync(l => l.Id == recovery.LoanId);
            if (loan == null) continue;
            recovery.RecoveryStatus = "REVERSED";
            recovery.UpdatedAt = DateTime.UtcNow;
            recovery.UpdatedBy = _current.UserId;
            loan.RecoveredAmount = WeightCalculator.R2(loan.RecoveredAmount - recovery.RecoveryAmount);
            loan.OutstandingAmount = WeightCalculator.R2(loan.OutstandingAmount + recovery.RecoveryAmount);
            if (loan.LoanStatus == "CLOSED") loan.LoanStatus = "ACTIVE";
            loan.UpdatedAt = DateTime.UtcNow;
            loan.UpdatedBy = _current.UserId;
        }

        payment.PaymentStatus = "CANCELLED";
        payment.CancelReason = reason;
        payment.UpdatedAt = DateTime.UtcNow;
        payment.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("Cancel", "Payment", "Payment", id.ToString(), oldValue: "COMPLETED",
            newValue: new { Status = "CANCELLED", Reason = reason, purchasesRestored = paymentPurchases.Count, recoveriesReversed = recoveries.Count });
        return Ok(new
        {
            message = $"Payment {id} cancelled. {paymentPurchases.Count} purchase(s) are payable again. Reason recorded in audit log.",
            purchasesRestored = paymentPurchases.Count,
            recoveriesReversed = recoveries.Count
        });
    }

    /// <summary>Cash Evidence photos already captured for this Payment.</summary>
    [HttpGet("{id:int}/images")]
    public async Task<IActionResult> Images(int id)
    {
        if (!_current.HasPermission("CashEvidence.View"))
            return StatusCode(403, new { message = "You do not have 'CashEvidence.View' permission." });
        if (!await _db.Payments.AnyAsync(p => p.Id == id && !p.IsDeleted))
            return NotFound(new { message = "Payment not found." });
        var images = await _db.PaymentImages.Where(i => i.PaymentId == id && i.Status)
            .OrderBy(i => i.ImageName)
            .Select(i => new { i.Id, i.CameraId, i.ImageName, i.FileHash, i.CapturedAt, i.CapturedBy })
            .ToListAsync();
        return Ok(images);
    }

    /// <summary>Manually (re-)trigger Cash Evidence capture for this Payment.</summary>
    [HttpPost("{id:int}/images/capture")]
    public async Task<IActionResult> CaptureImages(int id, [FromServices] ICameraCaptureService capture, CancellationToken ct)
    {
        if (!_current.HasPermission("CashEvidence.Create"))
            return StatusCode(403, new { message = "You do not have 'CashEvidence.Create' permission." });
        if (!await _db.Payments.AnyAsync(p => p.Id == id && !p.IsDeleted))
            return NotFound(new { message = "Payment not found." });
        var results = await capture.CaptureForPaymentAsync(id, _current.UserId, ct);
        var okCount = results.Count(r => r.Success);
        return Ok(new
        {
            message = results.Count == 0
                ? "Camera capture is disabled system-wide, or no cameras are enabled for capture."
                : $"Captured {okCount} of {results.Count} camera(s) for Cash Evidence.",
            results
        });
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
