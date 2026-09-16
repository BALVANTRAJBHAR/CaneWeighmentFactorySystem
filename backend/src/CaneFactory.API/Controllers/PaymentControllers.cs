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
using System.Security.Cryptography;

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
    private readonly ISmsService _sms;

    public PaymentController(AppDbContext db, IAuditService audit, ICurrentUser current,
        ISequenceGenerator seq, IMemoryCache cache, IServiceScopeFactory scopeFactory, ISmsService sms)
    {
        _db = db; _audit = audit; _current = current; _seq = seq; _cache = cache; _scopeFactory = scopeFactory; _sms = sms;
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
        if (cfg == null) return null;
        return new
        {
            printerType = cfg.PrinterType,
            printerName = cfg.PrinterName,
            copies = cfg.PaymentCopies,
            language = cfg.Language,
            shouldAutoPrint = cfg.AutoPrint && cfg.PaymentCopies > 0,
            // When physical printing is disabled, always return an A4 PDF URL for the operator.
            documentUrl = $"/api/print/payment/{paymentId}?format=final" +
                (cfg.AutoPrint && cfg.PaymentCopies > 0 ? "" : "&target=A4")
        };
    }

    /// <summary>Purchases eligible for payment for a Grower: TARE_DONE, PaymentStatus=PENDING, UNLOCKED.
    /// SINGLE narrows to one PurchaseId, DATE_RANGE narrows by TareDateTime, FARMER takes all of them.</summary>
    private IQueryable<Purchase> EligiblePurchasesQuery(string mode, int growerId, int? purchaseId, DateTime? from, DateTime? to)
    {
        var q = _db.Purchases.Where(p => p.GrowerId == growerId && !p.IsDeleted
            && p.GrossTareStatus == "TARE_DONE" && p.PaymentStatus == "PENDING" && p.LockStatus == "UNLOCKED");
        if (mode == "SINGLE") return q.Where(p => p.Id == purchaseId!.Value);
        if (mode is "DATE_RANGE" or "FARMER")
        {
            if (from.HasValue) q = q.Where(p => p.TareDateTime >= from.Value.Date);
            // Use an exclusive next-day boundary: SQL datetime values later on To Date must be included.
            if (to.HasValue) q = q.Where(p => p.TareDateTime < to.Value.Date.AddDays(1));
        }
        return q;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? growerCode, [FromQuery] string? status,
        [FromQuery] int? adviceNumber, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (Deny("View") is { } d) return d;
        var q = await ScopedQueryAsync();
        if (!string.IsNullOrWhiteSpace(growerCode))
        {
            var search = growerCode.Trim();
            q = q.Where(p => p.GrowerCode.Contains(search) || p.Grower.GrowerName.Contains(search));
        }
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
    public async Task<IActionResult> EligiblePurchases([FromQuery] string selectionMode, [FromQuery] string? growerCode,
        [FromQuery] int? purchaseId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        if (Deny("View") is { } d) return d;
        var mode = (selectionMode ?? "").Trim().ToUpperInvariant();
        if (mode is not ("SINGLE" or "DATE_RANGE" or "FARMER"))
            return BadRequest(new { message = "selectionMode must be SINGLE, DATE_RANGE or FARMER." });
        if (mode == "SINGLE" && !purchaseId.HasValue)
            return BadRequest(new { message = "purchaseId is required for SINGLE selection mode." });

        Grower? grower;
        try { grower = await ResolveSelectionGrowerAsync(mode, growerCode, purchaseId, fromDate, toDate); }
        catch (PaymentSelectionException ex) { return Conflict(new { message = ex.Message }); }
        if (grower == null) return NotFound(new { message = mode == "SINGLE"
            ? $"Purchase ID {purchaseId} does not exist." : "No eligible grower was found for the selected criteria." });

        if (mode == "SINGLE")
        {
            var selected = await _db.Purchases.AsNoTracking().FirstAsync(p => p.Id == purchaseId && !p.IsDeleted);
            if (selected.PaymentStatus == "PAID")
                return Ok(new { growerCode = grower.GrowerCode, eligiblePurchases = Array.Empty<object>(),
                    outstandingLoans = Array.Empty<object>(), totalPurchaseAmount = 0m, totalOutstandingLoan = 0m,
                    estimatedLoanDeduction = 0m, estimatedNetPayable = 0m, alreadyPaid = true,
                    statusMessage = $"Payment already done for Purchase ID {purchaseId}." });
        }

        var eligible = await EligiblePurchasesQuery(mode, grower.Id, purchaseId, fromDate, toDate)
            .OrderBy(p => p.GrossDateTime)
            .Select(p => new { purchaseId = p.Id, p.VehicleNumber, p.FinalWeightQuintal, p.Rate, p.PurchaseAmount, p.TareDateTime })
            .ToListAsync();
        var totalPurchaseAmount = WeightCalculator.R2(eligible.Sum(p => p.PurchaseAmount ?? 0));
        var totalFinalWeight = WeightCalculator.R2(eligible.Sum(p => p.FinalWeightQuintal ?? 0));
        var outstandingLoans = await _db.Loans.Where(l => l.GrowerCode == grower.GrowerCode && l.LoanStatus == "ACTIVE" && !l.IsDeleted)
            .OrderBy(l => l.IssueDate)
            .Select(l => new { loanId = l.Id, l.OutstandingAmount }).ToListAsync();
        var estimatedDeduction = WeightCalculator.R2(Math.Min(outstandingLoans.Sum(l => l.OutstandingAmount), totalPurchaseAmount));
        return Ok(new
        {
            growerCode = grower.GrowerCode,
            eligiblePurchases = eligible,
            purchaseCount = eligible.Count,
            totalFinalWeight,
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

        Grower? grower;
        try { grower = await ResolveSelectionGrowerAsync(mode, req.GrowerCode, req.PurchaseId, req.FromDate, req.ToDate, includeVillage: true); }
        catch (PaymentSelectionException ex) { return Conflict(new { message = ex.Message }); }
        if (grower == null) return NotFound(new { message = mode == "SINGLE"
            ? $"Purchase ID {req.PurchaseId} does not exist." : "No eligible grower was found for the selected criteria." });

        var paymentMode = await _db.PaymentModes.FirstOrDefaultAsync(m => m.Id == req.PaymentModeId && !m.IsDeleted && m.Status);
        if (paymentMode == null) return BadRequest(new { message = "Selected Payment Mode does not exist or is inactive." });
        var isCashPayment = string.Equals(paymentMode.ModeCode, "CASH", StringComparison.OrdinalIgnoreCase);

        var eligible = await EligiblePurchasesQuery(mode, grower.Id, req.PurchaseId, req.FromDate, req.ToDate).ToListAsync();
        if (eligible.Count == 0) return Conflict(new { message = "No payable purchases found for the selected criteria." });

        var season = await _db.Seasons.FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted);
        if (season == null) return Conflict(new { message = "No ACTIVE season is configured. Ask Admin/Developer to activate a Season." });

        var totalPurchaseAmount = WeightCalculator.R2(eligible.Sum(p => p.PurchaseAmount ?? 0));

        int paymentId = 0;
        int adviceNumber = 0;
        decimal totalDeducted = 0;
        decimal netPayable = 0;
        Payment? payment = null;
        await _db.ExecuteInTransactionAsync(async () =>
        {
        paymentId = (int)await _seq.NextAsync("PaymentId", 1);
        adviceNumber = (int)await _seq.NextAsync("AdviceNumber", 1);

        // Automatic Loan deduction - FIFO (oldest ACTIVE loan first), never exceeding the payable amount.
        var remaining = totalPurchaseAmount;
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
        netPayable = WeightCalculator.R2(totalPurchaseAmount - totalDeducted);

        payment = new Payment
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
        _db.Payments.Add(payment!);

        // Cash Book is the source of truth for physical cash. A cash farmer payment is
        // automatically posted as an outflow in the same transaction as the payment itself.
        if (isCashPayment && netPayable > 0)
        {
            _db.CashBookEntries.Add(new CashBookEntry
            {
                EntryDate = payment.PaymentDate.Date,
                EntryType = "CASH_OUT",
                SourceType = "FARMER_PAYMENT",
                SourceName = "Farmer cash payment",
                Amount = netPayable,
                PaymentId = paymentId,
                GrowerId = grower.Id,
                GrowerCode = grower.GrowerCode,
                GrowerName = grower.GrowerName,
                NetPayableAmount = netPayable,
                ReferenceNumber = $"PAY-{paymentId}",
                Remarks = $"Advice {adviceNumber}",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _current.UserId,
                Status = true
            });
        }

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
        });

        await _audit.LogAsync("Pay", "Payment", "Payment", paymentId.ToString(),
            newValue: new { payment!.GrowerCode, payment.AdviceNumber, payment.TotalPurchaseAmount, payment.LoanDeductedAmount, payment.NetPayableAmount, purchaseCount = eligible.Count });

        var isCash = isCashPayment;
        if (isCash) QueueCapture(paymentId);

        var smsQueued = false;
        try
        {
            smsQueued = await _sms.QueueAsync("PAYMENT_COMPLETED", grower.Id, grower.Mobile, $"PAY-{paymentId}", new Dictionary<string, string>
            {
                ["GrowerName"] = grower.GrowerName,
                ["GrowerCode"] = grower.GrowerCode,
                ["AdviceNumber"] = adviceNumber.ToString(),
                ["TotalPurchaseAmount"] = totalPurchaseAmount.ToString("F2"),
                ["LoanDeducted"] = totalDeducted.ToString("F2"),
                ["NetPayable"] = netPayable.ToString("F2"),
                ["PaymentMode"] = paymentMode.ModeName
            });
        }
        catch { /* SMS is a notification only - never affects a successful payment */ }

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
            smsQueued
        });
    }

    /// <summary>Resolves the owner of a payment selection without trusting a grower code for a single purchase.</summary>
    private async Task<Grower?> ResolveSelectionGrowerAsync(string mode, string? growerCode, int? purchaseId,
        DateTime? fromDate, DateTime? toDate, bool includeVillage = false)
    {
        if (mode == "SINGLE")
        {
            var purchase = await _db.Purchases.AsNoTracking().FirstOrDefaultAsync(p => p.Id == purchaseId && !p.IsDeleted);
            if (purchase == null) return null;
            return includeVillage
                ? await _db.Growers.Include(g => g.Village).FirstOrDefaultAsync(g => g.Id == purchase.GrowerId && !g.IsDeleted)
                : await _db.Growers.FirstOrDefaultAsync(g => g.Id == purchase.GrowerId && !g.IsDeleted);
        }

        if (!string.IsNullOrWhiteSpace(growerCode))
        {
            var text = growerCode.Trim();
            var matches = await _db.Growers.Where(g => !g.IsDeleted
                    && (g.GrowerCode == text || g.GrowerName.Contains(text) || g.Mobile == text))
                .OrderBy(g => g.GrowerCode).Take(2).ToListAsync();
            if (matches.Count > 1)
                throw new PaymentSelectionException("More than one grower matches this name. Enter the Grower Code or full mobile number.");
            if (matches.Count == 0) return null;
            if (!includeVillage) return matches[0];
            return await _db.Growers.Include(g => g.Village)
                .FirstOrDefaultAsync(g => g.Id == matches[0].Id && !g.IsDeleted);
        }

        if (mode != "DATE_RANGE") return null;
        var candidates = _db.Purchases.AsNoTracking().Where(p => !p.IsDeleted && p.GrossTareStatus == "TARE_DONE"
            && p.PaymentStatus == "PENDING" && p.LockStatus == "UNLOCKED");
        if (fromDate.HasValue) candidates = candidates.Where(p => p.TareDateTime >= fromDate.Value.Date);
        if (toDate.HasValue) candidates = candidates.Where(p => p.TareDateTime < toDate.Value.Date.AddDays(1));
        var growerIds = await candidates.Select(p => p.GrowerId).Distinct().Take(2).ToListAsync();
        if (growerIds.Count == 0) return null;
        if (growerIds.Count > 1) throw new PaymentSelectionException("The selected date range has payable purchases for multiple growers. Use Farmer-wise to pay one grower at a time.");
        return includeVillage
            ? await _db.Growers.Include(g => g.Village).FirstOrDefaultAsync(g => g.Id == growerIds[0] && !g.IsDeleted)
            : await _db.Growers.FirstOrDefaultAsync(g => g.Id == growerIds[0] && !g.IsDeleted);
    }

    private sealed class PaymentSelectionException : Exception
    {
        public PaymentSelectionException(string message) : base(message) { }
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

        // Reversing a cash farmer payment restores physical cash to the Cash Book.
        // Keep both rows rather than deleting history, so reconciliation/audit stays intact.
        var originalCashOut = await _db.CashBookEntries.FirstOrDefaultAsync(x => x.PaymentId == id
            && !x.IsDeleted && x.EntryType == "CASH_OUT" && x.SourceType == "FARMER_PAYMENT");
        if (originalCashOut != null)
        {
            _db.CashBookEntries.Add(new CashBookEntry
            {
                EntryDate = DateTime.UtcNow.Date,
                EntryType = "CASH_IN",
                SourceType = "PAYMENT_REVERSAL",
                SourceName = "Cancelled farmer cash payment",
                Amount = originalCashOut.Amount,
                PaymentId = id,
                GrowerId = originalCashOut.GrowerId,
                GrowerCode = originalCashOut.GrowerCode,
                GrowerName = originalCashOut.GrowerName,
                NetPayableAmount = originalCashOut.NetPayableAmount,
                ReferenceNumber = $"PAY-{id}-REVERSAL",
                Remarks = $"Payment cancelled: {reason}",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _current.UserId,
                Status = true
            });
        }
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

    [HttpPost("{id:int}/images/upload")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadImage(int id, IFormFile image, CancellationToken ct)
    {
        if (!_current.HasPermission("CashEvidence.Create"))
            return StatusCode(403, new { message = "You do not have 'CashEvidence.Create' permission." });
        if (image == null || image.Length == 0 || image.Length > 10_000_000)
            return BadRequest(new { message = "Select an image up to 10 MB." });
        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png"))
            return BadRequest(new { message = "Only JPG, JPEG or PNG evidence images are allowed." });
        var payment = await _db.Payments.Include(p => p.Season)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
        if (payment == null) return NotFound(new { message = "Payment not found." });
        var setting = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == "ImageStorageRoot", ct);
        var root = string.IsNullOrWhiteSpace(setting?.Value)
            ? Path.Combine(Path.GetTempPath(), "CanePaymentData") : setting.Value;
        var now = DateTime.Now;
        var folder = Path.Combine(root, payment.Season?.SeasonName ?? "Default", "PaymentImages",
            now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"), $"PAY-{id}");
        Directory.CreateDirectory(folder);
        var imageName = $"CASH-UPLOAD-{now:HHmmssfff}-{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(folder, imageName);
        await using (var output = System.IO.File.Create(filePath))
            await image.CopyToAsync(output, ct);
        var bytes = await System.IO.File.ReadAllBytesAsync(filePath, ct);
        var record = new PaymentImage { PaymentId = id, CameraId = 0, ImageName = imageName, FilePath = filePath,
            FileHash = Convert.ToHexString(SHA256.HashData(bytes)), CapturedAt = DateTime.UtcNow, CapturedBy = _current.UserId };
        _db.PaymentImages.Add(record);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("ImageUploaded", "CashEvidence", "Payment", id.ToString(), newValue: new { record.Id, imageName });
        return Ok(new { message = "Cash evidence uploaded.", imageId = record.Id, imageName });
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
