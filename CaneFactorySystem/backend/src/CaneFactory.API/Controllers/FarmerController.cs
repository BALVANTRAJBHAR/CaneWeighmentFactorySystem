using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

/// <summary>
/// Phase 12: Farmer self-service portal. Every endpoint here resolves the caller's OWN Grower
/// record strictly via the logged-in User's Mobile number (JWT-authenticated identity, never a
/// client-supplied growerId/growerCode) - a farmer can never view another farmer's data through
/// this controller, and an account with no matching Grower record gets a clear 404, not someone
/// else's data. Read-only by design: no POST/PUT/DELETE anywhere in this controller.
/// </summary>
[ApiController]
[Authorize]
[Route("api/farmer")]
public class FarmerController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IReportExportService _export;

    public FarmerController(AppDbContext db, ICurrentUser current, IReportExportService export)
    {
        _db = db; _current = current; _export = export;
    }

    private async Task<Domain.Entities.Grower?> MyGrowerAsync()
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == _current.UserId);
        if (user == null) return null;
        return await _db.Growers.Include(g => g.Village).Include(g => g.Bank)
            .FirstOrDefaultAsync(g => g.Mobile == user.Mobile && !g.IsDeleted);
    }

    /// <summary>Single-call dashboard: profile + purchase/payment/loan summary + last 5 of each.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var grower = await MyGrowerAsync();
        if (grower == null)
            return NotFound(new { message = "No Grower profile is linked to this account. Contact the factory office to link your mobile number." });

        var purchases = _db.Purchases.Where(p => p.GrowerId == grower.Id && !p.IsDeleted);
        var payments = _db.Payments.Where(p => p.GrowerId == grower.Id && !p.IsDeleted);
        var loans = _db.Loans.Where(l => l.GrowerId == grower.Id && !l.IsDeleted);

        var totalVehicles = await purchases.CountAsync();
        var pendingPayment = await purchases.CountAsync(p => p.PaymentStatus == "PENDING");
        var totalFinalWeight = (await purchases.Where(p => p.FinalWeightQuintal != null).Select(p => p.FinalWeightQuintal).ToListAsync()).Sum(x => x ?? 0);
        var totalPurchaseAmount = (await purchases.Where(p => p.PurchaseAmount != null).Select(p => p.PurchaseAmount).ToListAsync()).Sum(x => x ?? 0);
        var totalPaidAmount = (await payments.Where(p => p.PaymentStatus == "COMPLETED").Select(p => p.NetPayableAmount).ToListAsync()).Sum();
        var activeLoans = await loans.Where(l => l.LoanStatus == "ACTIVE").ToListAsync();
        var totalOutstandingLoan = activeLoans.Sum(l => l.OutstandingAmount);

        var recentPurchases = await purchases.OrderByDescending(p => p.Id).Take(5)
            .Select(p => new { p.Id, p.VehicleNumber, p.GrossDateTime, p.FinalWeightQuintal, p.PurchaseAmount, p.PaymentStatus, p.GrossTareStatus })
            .ToListAsync();
        var recentPayments = await payments.OrderByDescending(p => p.Id).Take(5)
            .Select(p => new { paymentId = p.Id, p.AdviceNumber, p.NetPayableAmount, p.PaymentDate, p.PaymentStatus })
            .ToListAsync();
        var recentLoans = await loans.OrderByDescending(l => l.Id).Take(5)
            .Select(l => new { loanId = l.Id, l.LoanAmount, l.OutstandingAmount, l.IssueDate, l.LoanStatus })
            .ToListAsync();

        return Ok(new
        {
            profile = new
            {
                grower.GrowerCode, grower.GrowerName, grower.FatherName, VillageName = grower.Village.VillageName,
                grower.Mobile, grower.Email, BankName = grower.Bank?.BankName, AccountMasked = MaskAccount(grower.BankAccountNumber)
            },
            summary = new
            {
                totalVehicles, pendingPayment, totalFinalWeight, totalPurchaseAmount, totalPaidAmount,
                totalOutstandingLoan, activeLoanCount = activeLoans.Count
            },
            recentPurchases, recentPayments, recentLoans
        });
    }

    /// <summary>Combined Purchase + Payment + Loan ledger for a date range - the farmer's
    /// "passbook" statement. Supports on-screen JSON as well as PDF/Excel download.</summary>
    [HttpGet("statement")]
    public async Task<IActionResult> Statement([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate,
        [FromQuery] string format = "json")
    {
        if (format is not ("json" or "pdf" or "excel")) return BadRequest(new { message = "format must be json, pdf or excel." });
        var grower = await MyGrowerAsync();
        if (grower == null)
            return NotFound(new { message = "No Grower profile is linked to this account. Contact the factory office to link your mobile number." });

        var from = fromDate ?? DateTime.UtcNow.AddMonths(-3);
        var to = toDate ?? DateTime.UtcNow;

        var entries = new List<(DateTime Date, string Type, string Reference, string Details, decimal Amount)>();

        var purchases = await _db.Purchases.Where(p => p.GrowerId == grower.Id && !p.IsDeleted
                && p.GrossDateTime >= from && p.GrossDateTime <= to && p.PurchaseAmount != null)
            .Select(p => new { p.Id, p.GrossDateTime, p.VehicleNumber, p.FinalWeightQuintal, p.PurchaseAmount })
            .ToListAsync();
        entries.AddRange(purchases.Select(p => (p.GrossDateTime, "Purchase", $"PUR-{p.Id}",
            $"Vehicle {p.VehicleNumber}, {p.FinalWeightQuintal:F2} Qtl", p.PurchaseAmount ?? 0)));

        var payments = await _db.Payments.Where(p => p.GrowerId == grower.Id && !p.IsDeleted
                && p.PaymentDate >= from && p.PaymentDate <= to && p.PaymentStatus == "COMPLETED")
            .Select(p => new { p.Id, p.PaymentDate, p.AdviceNumber, p.NetPayableAmount })
            .ToListAsync();
        entries.AddRange(payments.Select(p => (p.PaymentDate, "Payment", $"PAY-{p.Id}",
            $"Advice No {p.AdviceNumber}", -p.NetPayableAmount)));

        var loanIssues = await _db.Loans.Where(l => l.GrowerId == grower.Id && !l.IsDeleted
                && l.IssueDate >= from && l.IssueDate <= to && l.LoanStatus != "CANCELLED")
            .Select(l => new { l.Id, l.IssueDate, l.LoanAmount })
            .ToListAsync();
        entries.AddRange(loanIssues.Select(l => (l.IssueDate, "Loan Issued", $"LOAN-{l.Id}", "Loan disbursed", l.LoanAmount)));

        var loanRecoveries = await _db.LoanRecoveries.Where(r => r.GrowerId == grower.Id && !r.IsDeleted
                && r.RecoveryDate >= from && r.RecoveryDate <= to && r.RecoveryStatus == "ACTIVE")
            .Select(r => new { r.Id, r.RecoveryDate, r.LoanId, r.RecoveryAmount })
            .ToListAsync();
        entries.AddRange(loanRecoveries.Select(r => (r.RecoveryDate, "Loan Recovery", $"LR-{r.Id}", $"Against Loan {r.LoanId}", -r.RecoveryAmount)));

        var sorted = entries.OrderBy(e => e.Date).ToList();
        var totalCredit = sorted.Where(e => e.Amount > 0).Sum(e => e.Amount);
        var totalDebit = -sorted.Where(e => e.Amount < 0).Sum(e => e.Amount);

        if (format == "json")
            return Ok(new
            {
                growerCode = grower.GrowerCode, growerName = grower.GrowerName, fromDate = from, toDate = to,
                entries = sorted.Select(e => new { date = e.Date, type = e.Type, reference = e.Reference, details = e.Details, amount = e.Amount }),
                totals = new { totalCredit, totalDebit, netBalance = totalCredit - totalDebit }
            });

        var headers = new List<string> { "Date", "Type", "Reference", "Details", "Amount (Rs)" };
        var tableRows = sorted.Select(e => new List<string> {
            e.Date.ToString("dd-MM-yyyy"), e.Type, e.Reference, e.Details, e.Amount.ToString("F2")
        }).ToList();
        var totals = new List<(string, string)>
        {
            ("Total Credit (Rs)", totalCredit.ToString("F2")),
            ("Total Debit (Rs)", totalDebit.ToString("F2")),
            ("Net Balance (Rs)", (totalCredit - totalDebit).ToString("F2"))
        };
        var title = $"Statement - {grower.GrowerName} ({grower.GrowerCode})";
        if (format == "pdf")
            return File(_export.ToPdf(title, $"{from:dd-MM-yyyy} to {to:dd-MM-yyyy}", headers, tableRows, totals), "application/pdf", "Farmer-Statement.pdf");
        return File(_export.ToExcel(title, headers, tableRows, totals),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Farmer-Statement.xlsx");
    }

    private static string? MaskAccount(string? acc) =>
        string.IsNullOrEmpty(acc) ? null : (acc.Length <= 4 ? acc : new string('X', acc.Length - 4) + acc[^4..]);
}
