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
            PaymentModeName = p.PaymentMode.ModeName, PaymentDate = p.PaymentDate, PaidByUserName = p.PaidByUserName,
            PaymentStatus = p.PaymentStatus
        });

        if (format == "json")
        {
            var items = await projected.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { items, totalCount = pCount, page, pageSize, totals = new { count = pCount, total = pTotal, deducted = pDeducted, net = pNet } });
        }

        var rows = await projected.Take(5000).ToListAsync();
        var headers = new List<string> { "Payment ID", "Advice No", "Grower Code", "Grower Name", "Village",
            "Total Purchase (Rs)", "Loan Deducted (Rs)", "Net Payable (Rs)", "Payment Mode", "Payment Date", "Paid By", "Status" };
        var tableRows = rows.Select(r => new List<string> {
            r.PaymentId.ToString(), r.AdviceNumber.ToString(), r.GrowerCode, r.GrowerName, r.VillageName,
            r.TotalPurchaseAmount.ToString("F2"), r.LoanDeductedAmount.ToString("F2"), r.NetPayableAmount.ToString("F2"),
            r.PaymentModeName, r.PaymentDate.ToString("dd-MM-yyyy"), r.PaidByUserName, r.PaymentStatus
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
