using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

/// <summary>
/// Phase 11: Reporting engine. Rather than 14 separate hard-coded endpoints, four flexible,
/// filterable, farmer-scoped endpoints cover every named report from the requirements:
/// Purchases (Daily Weighment/Purchase, Gross/Tare/Net, Village-wise, Grower-wise, Date-range,
/// Rate-wise, Variety-wise, Vehicle-wise, Pending Payment, Lock report), Payments (Payment +
/// Cancelled report), Loans (Loan + Cancelled report) and Daily Collection. The existing
/// /api/audit endpoint (Phase 1) covers the Audit report and gains PDF/Excel export here too.
/// Every endpoint supports date-range + filters + totals + format=json|pdf|excel.
/// </summary>
[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IReportExportService _export;

    public ReportsController(AppDbContext db, ICurrentUser current, IReportExportService export)
    {
        _db = db; _current = current; _export = export;
    }

    private IActionResult? Deny(string action) =>
        _current.HasPermission($"Report.{action}") ? null
            : StatusCode(403, new { message = $"You do not have 'Report.{action}' permission." });

    private bool IsFarmerOnly =>
        (_current.Role ?? "").Split(',').All(r => r is "Farmer" or "") && (_current.Role ?? "") != "";

    private static string ActionFor(string format) => format switch { "pdf" => "Print", "excel" => "Export", _ => "View" };

    private static IActionResult? ValidateFormat(string format) =>
        format is "json" or "pdf" or "excel" ? null : new BadRequestObjectResult(new { message = "format must be json, pdf or excel." });

    // Calendar dates are inclusive in the UI; compare timestamps to the next
    // midnight exclusively, retaining SQL index seeks and subsecond precision.
    private static IActionResult? ValidateDates(ref DateTime? from, ref DateTime? to)
    {
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            return new BadRequestObjectResult(new { message = "From Date must not be after To Date." });
        if (to?.Date == DateTime.MaxValue.Date)
            return new BadRequestObjectResult(new { message = "To Date must be before 31-12-9999." });
        from = from?.Date;
        to = to?.Date.AddDays(1);
        return null;
    }

    private IActionResult ExportFile(string reportName, string format, string title, string subtitle,
        List<string> headers, List<List<string>> rows, List<(string Label, string Value)> totals)
    {
        if (format == "pdf")
            return File(_export.ToPdf(title, subtitle, headers, rows, totals), "application/pdf", $"{reportName}.pdf");
        return File(_export.ToExcel(title, headers, rows, totals), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{reportName}.xlsx");
    }

    // --------------------------------------------------------------- CASH BOOK
    [HttpGet("cash-book")]
    public async Task<IActionResult> CashBook([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate,
        [FromQuery] string? search, [FromQuery] string format = "json")
    {
        if (Deny(ActionFor(format)) is { } denied) return denied;
        if (ValidateFormat(format) is { } formatError) return formatError;
        if (ValidateDates(ref fromDate, ref toDate) is { } dateError) return dateError;

        var allEntries = _db.CashBookEntries.AsNoTracking().Where(x => !x.IsDeleted);
        var openingEntries = allEntries;
        if (fromDate.HasValue) openingEntries = openingEntries.Where(x => x.EntryDate < fromDate.Value);
        else openingEntries = openingEntries.Where(_ => false);
        var openingReceived = await openingEntries.Where(x => x.EntryType == "CASH_IN").SumAsync(x => (decimal?)x.Amount) ?? 0;
        var openingPaid = await openingEntries.Where(x => x.EntryType == "CASH_OUT").SumAsync(x => (decimal?)x.Amount) ?? 0;
        var openingBalance = openingReceived - openingPaid;

        var q = allEntries;
        if (fromDate.HasValue) q = q.Where(x => x.EntryDate >= fromDate.Value);
        if (toDate.HasValue) q = q.Where(x => x.EntryDate < toDate.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(x => (x.SourceName != null && x.SourceName.Contains(term)) ||
                (x.GrowerCode != null && x.GrowerCode.Contains(term)) ||
                (x.GrowerName != null && x.GrowerName.Contains(term)) ||
                (x.ReferenceNumber != null && x.ReferenceNumber.Contains(term)));
        }
        var entries = await q.OrderBy(x => x.EntryDate).ThenBy(x => x.Id).Take(5000).ToListAsync();
        var received = entries.Where(x => x.EntryType == "CASH_IN").Sum(x => x.Amount);
        var paid = entries.Where(x => x.EntryType == "CASH_OUT").Sum(x => x.Amount);
        var farmerCashPaid = entries.Where(x => x.EntryType == "CASH_OUT" && x.SourceType == "FARMER_PAYMENT").Sum(x => x.Amount);
        var otherCashPaid = entries.Where(x => x.EntryType == "CASH_OUT" && x.SourceType == "OTHER_CASH_PAYMENT").Sum(x => x.Amount);
        var closingBalance = openingBalance + received - paid;

        if (format == "json")
        {
            var running = openingBalance;
            var items = entries.Select(x =>
            {
                running += x.EntryType == "CASH_IN" ? x.Amount : -x.Amount;
                return new
                {
                    id = x.Id, entryDate = x.EntryDate, entryType = x.EntryType, sourceType = x.SourceType,
                    sourceName = x.SourceName, amount = x.Amount, paymentId = x.PaymentId,
                    growerId = x.GrowerId, growerCode = x.GrowerCode, growerName = x.GrowerName,
                    netPayableAmount = x.NetPayableAmount, referenceNumber = x.ReferenceNumber,
                    remarks = x.Remarks, runningBalance = running
                };
            }).ToList();
            return Ok(new { items, totalCount = entries.Count, totals = new { openingBalance, received, paid, closingBalance } });
        }

        var rows = new List<List<string>>();
        var balance = openingBalance;
        foreach (var x in entries)
        {
            var inAmount = x.EntryType == "CASH_IN" ? x.Amount : 0;
            var outAmount = x.EntryType == "CASH_OUT" ? x.Amount : 0;
            balance += inAmount - outAmount;
            rows.Add(new List<string>
            {
                x.EntryDate.ToString("dd-MM-yyyy"), CashEntryLabel(x),
                x.SourceType, x.SourceName ?? "-",
                string.IsNullOrWhiteSpace(x.GrowerCode) ? "-" : $"{x.GrowerCode} {x.GrowerName}",
                x.PaymentId?.ToString() ?? "-", inAmount == 0 ? "-" : inAmount.ToString("F2"),
                outAmount == 0 ? "-" : outAmount.ToString("F2"), balance.ToString("F2"), x.ReferenceNumber ?? "-"
            });
        }
        var totals = new List<(string, string)>
        {
            ("Opening Cash (Rs)", openingBalance.ToString("F2")),
            ("Cash Received (Rs)", received.ToString("F2")),
            ("Cash Paid to Farmers (Rs)", farmerCashPaid.ToString("F2")),
            ("Other Cash Payments (Rs)", otherCashPaid.ToString("F2")),
            ("Total Cash Paid (Rs)", paid.ToString("F2")),
            ("Closing Cash Balance (Rs)", closingBalance.ToString("F2"))
        };
        return ExportFile("Cash-Book-Report", format, "Cash Book Report",
            $"Generated by {_current.Username} on {DateTime.UtcNow:dd-MM-yyyy HH:mm} UTC",
            new List<string> { "Date", "Entry", "Source", "Source Name", "Grower", "Payment ID", "Cash In (Rs)", "Cash Out (Rs)", "Balance (Rs)", "Reference" },
            rows, totals);
    }

    private static string CashEntryLabel(CaneFactory.Domain.Entities.CashBookEntry entry) =>
        entry.EntryType == "CASH_IN" ? "Received" :
        entry.SourceType == "FARMER_PAYMENT" ? "Farmer Payment" : "Other Cash Payment";

    // ---------------------------------------------------------- PROFIT / LOSS
    /// <summary>Profit = completed sale-purchase amount - completed cane purchase amount - expenses.</summary>
    [HttpGet("profit-loss")]
    public async Task<IActionResult> ProfitLoss([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate,
        [FromQuery] string format = "json")
    {
        if (Deny(ActionFor(format)) is { } denied) return denied;
        if (ValidateFormat(format) is { } formatError) return formatError;
        if (ValidateDates(ref fromDate, ref toDate) is { } dateError) return dateError;

        var purchases = _db.Purchases.AsNoTracking()
            .Where(x => !x.IsDeleted && x.GrossTareStatus == "TARE_DONE" && x.PurchaseAmount != null);
        if (fromDate.HasValue) purchases = purchases.Where(x => x.TareDateTime >= fromDate);
        if (toDate.HasValue) purchases = purchases.Where(x => x.TareDateTime < toDate);

        var sales = _db.SalePurchases.AsNoTracking()
            .Where(x => !x.IsDeleted && x.WeighmentStatus == "COMPLETED" && x.Amount != null);
        if (fromDate.HasValue) sales = sales.Where(x => x.GrossDateTime >= fromDate);
        if (toDate.HasValue) sales = sales.Where(x => x.GrossDateTime < toDate);

        var expenses = _db.Expenses.AsNoTracking().Where(x => !x.IsDeleted);
        if (fromDate.HasValue) expenses = expenses.Where(x => x.ExpenseDate >= fromDate);
        if (toDate.HasValue) expenses = expenses.Where(x => x.ExpenseDate < toDate);

        var canePurchaseAmount = await purchases.SumAsync(x => (decimal?)x.PurchaseAmount) ?? 0;
        var salePurchaseAmount = await sales.SumAsync(x => (decimal?)x.Amount) ?? 0;
        var expenseAmount = await expenses.SumAsync(x => (decimal?)x.TotalAmount) ?? 0;
        var profit = salePurchaseAmount - canePurchaseAmount - expenseAmount;
        var expenseCount = await expenses.CountAsync();
        var saleCount = await sales.CountAsync();
        var purchaseCount = await purchases.CountAsync();
        var totals = new List<(string, string)>
        {
            ("Total Sale Purchase (Rs)", salePurchaseAmount.ToString("F2")),
            ("Total Cane Purchase (Rs)", canePurchaseAmount.ToString("F2")),
            ("Total Expense (Rs)", expenseAmount.ToString("F2")),
            ("Profit (Rs)", profit.ToString("F2"))
        };
        if (format == "json")
        {
            var detail = await expenses.Include(x => x.ExpenseType).OrderByDescending(x => x.ExpenseDate)
                .Take(5000).Select(x => new
                {
                    id = x.Id, expenseDate = x.ExpenseDate, expenseName = x.ExpenseType.ExpenseName,
                    quantity = x.Quantity, unitCharge = x.UnitCharge, totalAmount = x.TotalAmount, remarks = x.Remarks
                }).ToListAsync();
            return Ok(new
            {
                totals = new { salePurchaseAmount, canePurchaseAmount, expenseAmount, profit, saleCount, purchaseCount, expenseCount },
                items = detail
            });
        }

        var headers = new List<string> { "Metric", "Amount (Rs)", "Records", "Calculation" };
        var rows = new List<List<string>>
        {
            new() { "Total Sale Purchase", salePurchaseAmount.ToString("F2"), saleCount.ToString(), "Completed sale purchases" },
            new() { "Total Cane Purchase", canePurchaseAmount.ToString("F2"), purchaseCount.ToString(), "Completed cane purchases" },
            new() { "Total Expense", expenseAmount.ToString("F2"), expenseCount.ToString(), "Saved expenses" },
            new() { "Profit", profit.ToString("F2"), "-", "Sale Purchase - Cane Purchase - Expense" }
        };
        return ExportFile("Profit-Loss-Report", format, "Profit / Loss Report",
            $"Generated by {_current.Username} on {DateTime.UtcNow:dd-MM-yyyy HH:mm} UTC", headers, rows, totals);
    }

    // ---------------------------------------------------------------- PURCHASES
    /// <summary>Covers Daily Weighment/Purchase, Gross/Tare/Net, Village-wise, Grower-wise,
    /// Date-range, Rate-wise, Variety-wise, Vehicle-wise, Pending Payment and Lock reports -
    /// all are this same dataset filtered/sorted differently by the caller.</summary>
    [HttpGet("purchases")]
    public async Task<IActionResult> Purchases(
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] int? villageId,
        [FromQuery] string? growerCode, [FromQuery] int? varietyTypeId, [FromQuery] int? varietyId,
        [FromQuery] int? vehicleTypeId, [FromQuery] decimal? rateMin, [FromQuery] decimal? rateMax,
        [FromQuery] string? paymentStatus, [FromQuery] string? lockStatus, [FromQuery] string? grossTareStatus,
        [FromQuery] string sortBy = "grossDateTime", [FromQuery] bool desc = true,
        [FromQuery] string format = "json", [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        if (Deny(ActionFor(format)) is { } d) return d;
        if (ValidateFormat(format) is { } fe) return fe;

        if (ValidateDates(ref fromDate, ref toDate) is { } dateError) return dateError;
        var q = _db.Purchases.Where(p => !p.IsDeleted);
        if (IsFarmerOnly)
        {
            var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == _current.UserId);
            q = q.Where(p => p.Grower.Mobile == user.Mobile);
        }
        else if (!string.IsNullOrWhiteSpace(growerCode)) q = q.Where(p => p.GrowerCode == growerCode.Trim());
        if (villageId.HasValue) q = q.Where(p => p.VillageId == villageId);
        if (fromDate.HasValue) q = q.Where(p => p.TareDateTime >= fromDate);
        if (toDate.HasValue) q = q.Where(p => p.TareDateTime < toDate);
        if (varietyTypeId.HasValue) q = q.Where(p => p.VarietyTypeId == varietyTypeId);
        if (varietyId.HasValue) q = q.Where(p => p.VarietyId == varietyId);
        if (vehicleTypeId.HasValue) q = q.Where(p => p.VehicleTypeId == vehicleTypeId);
        if (rateMin.HasValue) q = q.Where(p => p.Rate >= rateMin);
        if (rateMax.HasValue) q = q.Where(p => p.Rate <= rateMax);
        if (!string.IsNullOrWhiteSpace(paymentStatus)) q = q.Where(p => p.PaymentStatus == paymentStatus);
        if (!string.IsNullOrWhiteSpace(lockStatus)) q = q.Where(p => p.LockStatus == lockStatus);
        if (!string.IsNullOrWhiteSpace(grossTareStatus)) q = q.Where(p => p.GrossTareStatus == grossTareStatus);

        q = sortBy switch
        {
            "villageName" => desc ? q.OrderByDescending(p => p.Grower.Village.VillageName) : q.OrderBy(p => p.Grower.Village.VillageName),
            "growerName" => desc ? q.OrderByDescending(p => p.Grower.GrowerName) : q.OrderBy(p => p.Grower.GrowerName),
            "varietyName" => desc ? q.OrderByDescending(p => p.Variety.VarietyName) : q.OrderBy(p => p.Variety.VarietyName),
            "vehicleNumber" => desc ? q.OrderByDescending(p => p.VehicleNumber) : q.OrderBy(p => p.VehicleNumber),
            "rate" => desc ? q.OrderByDescending(p => p.Rate) : q.OrderBy(p => p.Rate),
            _ => desc ? q.OrderByDescending(p => p.GrossDateTime) : q.OrderBy(p => p.GrossDateTime)
        };

        var totalCount = await q.CountAsync();
        var totalFinalWeight = (await q.Where(p => p.FinalWeightQuintal != null).Select(p => p.FinalWeightQuintal).ToListAsync()).Sum(x => x ?? 0);
        var totalPurchaseAmount = (await q.Where(p => p.PurchaseAmount != null).Select(p => p.PurchaseAmount).ToListAsync()).Sum(x => x ?? 0);

        var projected = q.Select(p => new PurchaseReportRow
        {
            PurchaseId = p.Id, GrowerCode = p.GrowerCode, GrowerName = p.Grower.GrowerName,
            VillageName = p.Grower.Village.VillageName, VehicleNumber = p.VehicleNumber, VarietyName = p.Variety.VarietyName,
            GrossWeightQuintal = p.GrossWeightQuintal, GrossDateTime = p.GrossDateTime,
            PurchaseDate = p.TareDateTime,
            TareWeightQuintal = p.TareWeightQuintal, CuttingWeightQuintal = p.CuttingWeightQuintal,
            NetWeightQuintal = p.NetWeightQuintal, FinalWeightQuintal = p.FinalWeightQuintal,
            Rate = p.Rate, PurchaseAmount = p.PurchaseAmount, GrossTareStatus = p.GrossTareStatus,
            PaymentStatus = p.PaymentStatus, LockStatus = p.LockStatus
        });

        var totals = new List<(string, string)>
        {
            ("Total Vehicles", totalCount.ToString()),
            ("Total Final Weight (Qtl)", totalFinalWeight.ToString("F2")),
            ("Total Purchase Amount (Rs)", totalPurchaseAmount.ToString("F2"))
        };

        if (format == "json")
        {
            var items = await projected.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { items, totalCount, page, pageSize, totals = new { totalCount, totalFinalWeight, totalPurchaseAmount } });
        }

        var rows = await projected.Take(5000).ToListAsync();
        var headers = new List<string> { "Purchase ID", "Grower Code", "Grower Name", "Village", "Vehicle No", "Variety",
            "Gross (Qtl)", "Tare (Qtl)", "Cutting Weight (Qtl)", "Net (Qtl)", "Final (Qtl)", "Rate", "Amount (Rs)", "Purchase Date (Tare)", "Weighment Status" };
        var tableRows = rows.Select(r => new List<string> {
            r.PurchaseId.ToString(), r.GrowerCode, r.GrowerName, r.VillageName, r.VehicleNumber, r.VarietyName,
            r.GrossWeightQuintal.ToString("F2"), r.TareWeightQuintal?.ToString("F2") ?? "-",
            r.CuttingWeightQuintal?.ToString("F2") ?? "-", r.NetWeightQuintal?.ToString("F2") ?? "-",
            r.FinalWeightQuintal?.ToString("F2") ?? "-", r.Rate.ToString("F2"), r.PurchaseAmount?.ToString("F2") ?? "-",
            r.PurchaseDate?.ToString("dd-MM-yyyy") ?? "-", r.GrossTareStatus
        }).ToList();
        return ExportFile("Purchase-Report", format, "Purchase / Weighment Report",
            $"Generated by {_current.Username} on {DateTime.UtcNow:dd-MM-yyyy HH:mm} UTC", headers, tableRows, totals);
    }

    // ---------------------------------------------------------------- PAYMENTS
    /// <summary>Covers the Payment report and the Cancel report (status=CANCELLED).</summary>
    [HttpGet("payments")]
    public async Task<IActionResult> Payments(
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] int? villageId,
        [FromQuery] string? growerCode, [FromQuery] int? paymentModeId, [FromQuery] string? status,
        [FromQuery] string format = "json", [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        if (Deny(ActionFor(format)) is { } d) return d;
        if (ValidateFormat(format) is { } fe) return fe;

        if (ValidateDates(ref fromDate, ref toDate) is { } dateError) return dateError;
        var q = _db.Payments.Where(p => !p.IsDeleted);
        if (IsFarmerOnly)
        {
            var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == _current.UserId);
            q = q.Where(p => p.Grower.Mobile == user.Mobile);
        }
        else if (!string.IsNullOrWhiteSpace(growerCode)) q = q.Where(p => p.GrowerCode == growerCode.Trim());
        if (villageId.HasValue) q = q.Where(p => p.VillageId == villageId);
        if (fromDate.HasValue) q = q.Where(p => p.PaymentDate >= fromDate);
        if (toDate.HasValue) q = q.Where(p => p.PaymentDate < toDate);
        if (paymentModeId.HasValue) q = q.Where(p => p.PaymentModeId == paymentModeId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.PaymentStatus == status);
        q = q.OrderByDescending(p => p.Id);

        var pCount = await q.CountAsync();
        var pAmounts = await q.Select(p => new { p.TotalPurchaseAmount, p.LoanDeductedAmount, p.NetPayableAmount }).ToListAsync();
        var pTotal = pAmounts.Sum(p => p.TotalPurchaseAmount);
        var pDeducted = pAmounts.Sum(p => p.LoanDeductedAmount);
        var pNet = pAmounts.Sum(p => p.NetPayableAmount);
        var totals = new List<(string, string)>
        {
            ("Total Payments", pCount.ToString()),
            ("Total Purchase Amount (Rs)", pTotal.ToString("F2")),
            ("Total Loan Deducted (Rs)", pDeducted.ToString("F2")),
            ("Total Net Payable (Rs)", pNet.ToString("F2"))
        };

        var projected = q.Select(p => new PaymentReportRow
        {
            PaymentId = p.Id, AdviceNumber = p.AdviceNumber, GrowerCode = p.GrowerCode, GrowerName = p.Grower.GrowerName,
            VillageName = p.Grower.Village.VillageName, TotalPurchaseAmount = p.TotalPurchaseAmount,
            LoanDeductedAmount = p.LoanDeductedAmount, NetPayableAmount = p.NetPayableAmount,
            PaymentModeName = p.PaymentMode.ModeName,
            AccountHolderName = p.AccountHolderNameAtPayment ?? (p.PaymentMode.ModeCode == "BANK" ? p.Grower.AccountHolderName : null),
            BankName = p.BankNameAtPayment ?? (p.PaymentMode.ModeCode == "BANK" ? p.Grower.Bank!.BankName : null),
            BankBranch = p.BankBranchAtPayment ?? (p.PaymentMode.ModeCode == "BANK" ? p.Grower.Bank!.BranchName : null),
            BankIfsc = p.BankIfscAtPayment ?? (p.PaymentMode.ModeCode == "BANK" ? p.Grower.Bank!.IFSC : null),
            BankAccountNumber = p.BankAccountNumberAtPayment ?? (p.PaymentMode.ModeCode == "BANK" ? p.Grower.BankAccountNumber : null),
            IsBankPayment = p.PaymentMode.ModeCode == "BANK",
            PaymentDate = p.PaymentDate, PaidByUserName = p.PaidByUserName,
            PaymentStatus = p.PaymentStatus
        });

        if (format == "json")
        {
            var items = await projected.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { items, totalCount = pCount, page, pageSize, totals = new { count = pCount, total = pTotal, deducted = pDeducted, net = pNet } });
        }

        var rows = await projected.Take(5000).ToListAsync();
        var headers = new List<string> { "Payment ID", "Advice No", "Grower Code", "Grower Name", "Village",
            "Total Purchase (Rs)", "Loan Deducted (Rs)", "Net Payable (Rs)", "Payment Mode", "Bank Details", "Payment Date", "Paid By", "Status" };
        var tableRows = rows.Select(r => new List<string> {
            r.PaymentId.ToString(), r.AdviceNumber.ToString(), r.GrowerCode, r.GrowerName, r.VillageName,
            r.TotalPurchaseAmount.ToString("F2"), r.LoanDeductedAmount.ToString("F2"), r.NetPayableAmount.ToString("F2"),
            r.PaymentModeName,
            r.IsBankPayment
                ? $"Name: {r.AccountHolderName ?? "-"}\nBank: {r.BankName ?? "-"}\nBranch: {r.BankBranch ?? "-"}\nIFSC: {r.BankIfsc ?? "-"}\nA/c: {r.BankAccountNumber ?? "-"}"
                : "-",
            r.PaymentDate.ToString("dd-MM-yyyy"), r.PaidByUserName, r.PaymentStatus
        }).ToList();
        return ExportFile("Payment-Report", format, "Payment Report",
            $"Generated by {_current.Username} on {DateTime.UtcNow:dd-MM-yyyy HH:mm} UTC", headers, tableRows, totals);
    }

    // ---------------------------------------------------------------- LOANS
    /// <summary>Covers the Loan report and the Cancel report (status=CANCELLED).</summary>
    [HttpGet("loans")]
    public async Task<IActionResult> Loans(
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] int? villageId,
        [FromQuery] string? growerCode, [FromQuery] int? loanTypeId, [FromQuery] string? status,
        [FromQuery] string format = "json", [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        if (Deny(ActionFor(format)) is { } d) return d;
        if (ValidateFormat(format) is { } fe) return fe;

        if (ValidateDates(ref fromDate, ref toDate) is { } dateError) return dateError;
        var q = _db.Loans.Where(l => !l.IsDeleted);
        if (IsFarmerOnly)
        {
            var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == _current.UserId);
            q = q.Where(l => l.Grower.Mobile == user.Mobile);
        }
        else if (!string.IsNullOrWhiteSpace(growerCode)) q = q.Where(l => l.GrowerCode == growerCode.Trim());
        if (villageId.HasValue) q = q.Where(l => l.VillageId == villageId);
        if (fromDate.HasValue) q = q.Where(l => l.IssueDate >= fromDate);
        if (toDate.HasValue) q = q.Where(l => l.IssueDate < toDate);
        if (loanTypeId.HasValue) q = q.Where(l => l.LoanTypeId == loanTypeId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(l => l.LoanStatus == status);
        q = q.OrderByDescending(l => l.Id);

        var lCount = await q.CountAsync();
        var lAmounts = await q.Select(l => new { l.LoanAmount, l.RecoveredAmount, l.OutstandingAmount }).ToListAsync();
        var lAmount = lAmounts.Sum(l => l.LoanAmount);
        var lRecovered = lAmounts.Sum(l => l.RecoveredAmount);
        var lOutstanding = lAmounts.Sum(l => l.OutstandingAmount);
        var totals = new List<(string, string)>
        {
            ("Total Loans", lCount.ToString()),
            ("Total Loan Amount (Rs)", lAmount.ToString("F2")),
            ("Total Recovered (Rs)", lRecovered.ToString("F2")),
            ("Total Outstanding (Rs)", lOutstanding.ToString("F2"))
        };

        var projected = q.Select(l => new LoanReportRow
        {
            LoanId = l.Id, GrowerCode = l.GrowerCode, GrowerName = l.Grower.GrowerName, VillageName = l.Grower.Village.VillageName,
            LoanTypeName = l.LoanType.LoanTypeName, LoanAmount = l.LoanAmount, RecoveredAmount = l.RecoveredAmount,
            OutstandingAmount = l.OutstandingAmount, IssueDate = l.IssueDate, IssuedByUserName = l.IssuedByUserName, LoanStatus = l.LoanStatus
        });

        if (format == "json")
        {
            var items = await projected.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { items, totalCount = lCount, page, pageSize, totals = new { count = lCount, amount = lAmount, recovered = lRecovered, outstanding = lOutstanding } });
        }

        var rows = await projected.Take(5000).ToListAsync();
        var headers = new List<string> { "Loan ID", "Grower Code", "Grower Name", "Village", "Loan Type",
            "Loan Amount (Rs)", "Recovered (Rs)", "Outstanding (Rs)", "Issue Date", "Issued By", "Status" };
        var tableRows = rows.Select(r => new List<string> {
            r.LoanId.ToString(), r.GrowerCode, r.GrowerName, r.VillageName, r.LoanTypeName,
            r.LoanAmount.ToString("F2"), r.RecoveredAmount.ToString("F2"), r.OutstandingAmount.ToString("F2"),
            r.IssueDate.ToString("dd-MM-yyyy"), r.IssuedByUserName, r.LoanStatus
        }).ToList();
        return ExportFile("Loan-Report", format, "Loan Report",
            $"Generated by {_current.Username} on {DateTime.UtcNow:dd-MM-yyyy HH:mm} UTC", headers, tableRows, totals);
    }

    // ---------------------------------------------------------------- DAILY COLLECTION
    [HttpGet("sale-purchases")]
    public async Task<IActionResult> SalePurchases(
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] int? itemId, [FromQuery] int? partyId,
        [FromQuery] string? vehicleNumber, [FromQuery] string? driverName, [FromQuery] int? salePurchaseId,
        [FromQuery] string? status, [FromQuery] string format = "json", [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        if (!_current.HasPermission("SalePurchase.View") || Deny(ActionFor(format)) is { } denied)
            return StatusCode(403, new { message = "You do not have permission to view or export SalePurchase reports." });
        if (ValidateFormat(format) is { } fe) return fe;
        if (ValidateDates(ref fromDate, ref toDate) is { } dateError) return dateError;
        var q = _db.SalePurchases.AsNoTracking().Where(x => !x.IsDeleted);
        // Either event must itself be inside the range; do not match a range
        // merely because tare is before it and gross is after it.
        if (fromDate.HasValue || toDate.HasValue)
            q = q.Where(x =>
                ((!fromDate.HasValue || x.TareDateTime >= fromDate) && (!toDate.HasValue || x.TareDateTime < toDate)) ||
                (x.GrossDateTime != null && (!fromDate.HasValue || x.GrossDateTime >= fromDate) && (!toDate.HasValue || x.GrossDateTime < toDate)));
        if (itemId.HasValue) q = q.Where(x => x.ItemId == itemId);
        if (partyId.HasValue) q = q.Where(x => x.PartyId == partyId);
        if (salePurchaseId.HasValue) q = q.Where(x => x.Id == salePurchaseId);
        if (!string.IsNullOrWhiteSpace(vehicleNumber)) q = q.Where(x => x.VehicleNumber.Contains(vehicleNumber.Trim()));
        if (!string.IsNullOrWhiteSpace(driverName)) q = q.Where(x => x.DriverName.Contains(driverName.Trim()));
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.WeighmentStatus == status.Trim().ToUpperInvariant());
        q = q.OrderByDescending(x => x.Id);
        var count = await q.CountAsync();
        var totalsData = await q.Select(x => new { x.TareWeightQuintal, x.GrossWeightQuintal, x.FinalWeightQuintal, x.Amount }).ToListAsync();
        var totalFinal = totalsData.Sum(x => x.FinalWeightQuintal ?? 0); var totalAmount = totalsData.Sum(x => x.Amount ?? 0);
        var projection = q.Select(x => new
        {
            salePurchaseId = x.Id, item = x.Item.ItemName, party = x.Party.PartyName, vehicleType = x.VehicleType.VehicleTypeName,
            x.VehicleNumber, driver = x.DriverName, x.TareWeightQuintal, x.GrossWeightQuintal, x.FinalWeightQuintal,
            x.Rate, x.Amount, tareOperator = x.TareByUserName, grossOperator = x.GrossByUserName,
            x.TareDateTime, x.GrossDateTime, status = x.WeighmentStatus
        });
        var totals = new List<(string, string)> { ("Total Records", count.ToString()), ("Total Final Weight (Qtl)", totalFinal.ToString("F2")), ("Total Amount (Rs)", totalAmount.ToString("F2")) };
        if (format == "json")
        {
            var items = await projection.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { items, totalCount = count, page, pageSize, totals = new { count, totalFinal, totalAmount } });
        }
        var rows = await projection.Take(5000).ToListAsync();
        var headers = new List<string> { "SalePurchase ID", "Item", "Party", "Vehicle Type", "Vehicle Number", "Driver", "Tare (Qtl)", "Gross (Qtl)", "Final (Qtl)", "Rate", "Amount", "Tare Operator", "Gross Operator", "Tare Date/Time", "Gross Date/Time", "Status" };
        var tableRows = rows.Select(x => new List<string> { x.salePurchaseId.ToString(), x.item, x.party, x.vehicleType, x.VehicleNumber, x.driver,
            x.TareWeightQuintal.ToString("F2"), x.GrossWeightQuintal?.ToString("F2") ?? "-", x.FinalWeightQuintal?.ToString("F2") ?? "-",
            x.Rate?.ToString("F2") ?? "-", x.Amount?.ToString("F2") ?? "-", x.tareOperator, x.grossOperator ?? "-",
            x.TareDateTime.ToString("dd-MM-yyyy"), x.GrossDateTime?.ToString("dd-MM-yyyy") ?? "-", x.status }).ToList();
        return ExportFile("SalePurchase-Report", format, "SalePurchase Weighment Report", $"Generated by {_current.Username} on {DateTime.UtcNow:dd-MM-yyyy HH:mm} UTC", headers, tableRows, totals);
    }

    // ---------------------------------------------------------------- DAILY COLLECTION
    [HttpGet("daily-collection")]
    public async Task<IActionResult> DailyCollection(
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] int? villageId,
        [FromQuery] string format = "json")
    {
        if (Deny(ActionFor(format)) is { } d) return d;
        if (ValidateFormat(format) is { } fe) return fe;

        if (ValidateDates(ref fromDate, ref toDate) is { } dateError) return dateError;
        var q = _db.Purchases.Where(p => !p.IsDeleted && p.GrossTareStatus != "CANCELLED" && p.TareDateTime != null);
        if (IsFarmerOnly)
        {
            var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == _current.UserId);
            q = q.Where(p => p.Grower.Mobile == user.Mobile);
        }
        if (villageId.HasValue) q = q.Where(p => p.VillageId == villageId);
        if (fromDate.HasValue) q = q.Where(p => p.TareDateTime >= fromDate);
        if (toDate.HasValue) q = q.Where(p => p.TareDateTime < toDate);

        var flat = await q.Select(p => new { p.TareDateTime, p.FinalWeightQuintal, p.PurchaseAmount }).ToListAsync();
        var rows = flat.GroupBy(p => p.TareDateTime!.Value.Date)
            .Select(g => new DailyCollectionRow
            {
                Date = g.Key,
                VehicleCount = g.Count(),
                FinalWeightQuintal = g.Sum(p => p.FinalWeightQuintal ?? 0),
                PurchaseAmount = g.Sum(p => p.PurchaseAmount ?? 0)
            }).OrderBy(r => r.Date).ToList();

        var totalVehicles = rows.Sum(r => r.VehicleCount);
        var totalFinalWeight = rows.Sum(r => r.FinalWeightQuintal);
        var totalPurchaseAmount = rows.Sum(r => r.PurchaseAmount);
        var totals = new List<(string, string)>
        {
            ("Total Vehicles", totalVehicles.ToString()),
            ("Total Final Weight (Qtl)", totalFinalWeight.ToString("F2")),
            ("Total Purchase Amount (Rs)", totalPurchaseAmount.ToString("F2"))
        };

        if (format == "json")
            return Ok(new { rows, totals = new { totalVehicles, totalFinalWeight, totalPurchaseAmount } });

        var headers = new List<string> { "Date", "Vehicle Count", "Final Weight (Qtl)", "Purchase Amount (Rs)" };
        var tableRows = rows.Select(r => new List<string> {
            r.Date.ToString("dd-MM-yyyy"), r.VehicleCount.ToString(), r.FinalWeightQuintal.ToString("F2"), r.PurchaseAmount.ToString("F2")
        }).ToList();
        return ExportFile("Daily-Collection-Report", format, "Daily Collection Report",
            $"Generated by {_current.Username} on {DateTime.UtcNow:dd-MM-yyyy HH:mm} UTC", headers, tableRows, totals);
    }
}

