using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

public sealed class CashBookReceiptRequest
{
    public DateTime? EntryDate { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? SourceName { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>Manual physical-cash expense/payment. Farmer payments are posted automatically
/// by PaymentController and must not be entered here.</summary>
public sealed class CashBookPaymentRequest
{
    public DateTime? EntryDate { get; set; }
    public string? PaidTo { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Remarks { get; set; }
}

[ApiController]
[Authorize]
[Route("api/cash-book")]
public class CashBookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAuditService _audit;

    public CashBookController(AppDbContext db, ICurrentUser current, IAuditService audit)
    {
        _db = db; _current = current; _audit = audit;
    }

    private IActionResult? Deny(string action) => _current.HasPermission($"CashBook.{action}")
        ? null : StatusCode(403, new { message = $"You do not have 'CashBook.{action}' permission." });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 200)
    {
        if (Deny("View") is { } denied) return denied;
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
            return BadRequest(new { message = "From Date must not be after To Date." });
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 500) pageSize = 200;
        var from = fromDate?.Date;
        var toExclusive = toDate?.Date.AddDays(1);
        var baseQuery = _db.CashBookEntries.AsNoTracking().Where(x => !x.IsDeleted);
        var openingQuery = baseQuery;
        if (from.HasValue) openingQuery = openingQuery.Where(x => x.EntryDate < from.Value);
        else openingQuery = openingQuery.Where(_ => false);
        var openingIn = await openingQuery.Where(x => x.EntryType == "CASH_IN").SumAsync(x => (decimal?)x.Amount) ?? 0;
        var openingOut = await openingQuery.Where(x => x.EntryType == "CASH_OUT").SumAsync(x => (decimal?)x.Amount) ?? 0;
        var openingBalance = openingIn - openingOut;

        var q = baseQuery;
        if (from.HasValue) q = q.Where(x => x.EntryDate >= from.Value);
        if (toExclusive.HasValue) q = q.Where(x => x.EntryDate < toExclusive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(x => (x.SourceName != null && x.SourceName.Contains(term)) ||
                (x.GrowerCode != null && x.GrowerCode.Contains(term)) ||
                (x.GrowerName != null && x.GrowerName.Contains(term)) ||
                (x.ReferenceNumber != null && x.ReferenceNumber.Contains(term)));
        }
        var received = await q.Where(x => x.EntryType == "CASH_IN").SumAsync(x => (decimal?)x.Amount) ?? 0;
        var paid = await q.Where(x => x.EntryType == "CASH_OUT").SumAsync(x => (decimal?)x.Amount) ?? 0;
        var farmerCashPaid = await q.Where(x => x.EntryType == "CASH_OUT" && x.SourceType == "FARMER_PAYMENT")
            .SumAsync(x => (decimal?)x.Amount) ?? 0;
        var otherCashPaid = await q.Where(x => x.EntryType == "CASH_OUT" && x.SourceType == "OTHER_CASH_PAYMENT")
            .SumAsync(x => (decimal?)x.Amount) ?? 0;
        var totalCount = await q.CountAsync();
        // Calculate the balance in chronological order, then display newest entries first.
        // This avoids a misleading balance when the visible table is sorted descending.
        var entries = await q.OrderBy(x => x.EntryDate).ThenBy(x => x.Id).ToListAsync();
        var running = openingBalance;
        var calculated = entries.Select(x =>
        {
            running += x.EntryType == "CASH_IN" ? x.Amount : -x.Amount;
            return new
            {
                id = x.Id, entryDate = x.EntryDate, entryType = x.EntryType, sourceType = x.SourceType,
                sourceName = x.SourceName, amount = x.Amount, paymentId = x.PaymentId, growerId = x.GrowerId,
                growerCode = x.GrowerCode, growerName = x.GrowerName, netPayableAmount = x.NetPayableAmount,
                referenceNumber = x.ReferenceNumber, remarks = x.Remarks, createdAt = x.CreatedAt, runningBalance = running
            };
        }).OrderByDescending(x => x.entryDate).ThenByDescending(x => x.id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Ok(new
        {
            items = calculated, totalCount, page, pageSize,
            totals = new { openingBalance, received, paid, farmerCashPaid, otherCashPaid,
                closingBalance = openingBalance + received - paid }
        });
    }

    /// <summary>Records physical cash received from Bank, Party or another external source.</summary>
    [HttpPost("receipts")]
    public async Task<IActionResult> ReceiveCash([FromBody] CashBookReceiptRequest request)
    {
        if (Deny("Create") is { } denied) return denied;
        var sourceType = (request.SourceType ?? string.Empty).Trim().ToUpperInvariant();
        if (sourceType is not ("BANK" or "PARTY" or "OTHER"))
            return BadRequest(new { message = "Source must be Bank, Party or Other." });
        if (string.IsNullOrWhiteSpace(request.SourceName))
            return BadRequest(new { message = "Source name is required." });
        if (request.Amount <= 0) return BadRequest(new { message = "Cash amount must be greater than zero." });
        if (request.Amount > 99_999_999.99m) return BadRequest(new { message = "Cash amount is too large." });

        var entry = new CashBookEntry
        {
            EntryDate = (request.EntryDate ?? DateTime.Now).Date,
            EntryType = "CASH_IN",
            SourceType = sourceType,
            SourceName = request.SourceName.Trim(),
            Amount = decimal.Round(request.Amount, 2),
            ReferenceNumber = string.IsNullOrWhiteSpace(request.ReferenceNumber) ? null : request.ReferenceNumber.Trim(),
            Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
            CreatedAt = DateTime.UtcNow, CreatedBy = _current.UserId, Status = true
        };
        _db.CashBookEntries.Add(entry);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "CashBook", nameof(CashBookEntry), entry.Id.ToString(), newValue: entry);
        return Ok(new { message = "Cash receipt saved in Cash Book.", id = entry.Id });
    }

    /// <summary>Records a non-farmer physical-cash payment and deducts it from the same ledger
    /// used by cash farmer payments. The row is immutable for audit/reconciliation.</summary>
    [HttpPost("payments")]
    public async Task<IActionResult> PayCash([FromBody] CashBookPaymentRequest request)
    {
        if (Deny("Create") is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(request.PaidTo))
            return BadRequest(new { message = "Paid-to name is required." });
        if (request.PaidTo.Trim().Length > 150)
            return BadRequest(new { message = "Paid-to name must be 150 characters or fewer." });
        if (request.Amount <= 0) return BadRequest(new { message = "Cash amount must be greater than zero." });
        if (request.Amount > 99_999_999.99m) return BadRequest(new { message = "Cash amount is too large." });
        if (string.IsNullOrWhiteSpace(request.Remarks))
            return BadRequest(new { message = "Remarks / purpose of payment is required." });
        if (request.Remarks.Trim().Length > 500)
            return BadRequest(new { message = "Remarks must be 500 characters or fewer." });

        var entry = new CashBookEntry
        {
            EntryDate = (request.EntryDate ?? DateTime.Now).Date,
            EntryType = "CASH_OUT",
            SourceType = "OTHER_CASH_PAYMENT",
            SourceName = request.PaidTo.Trim(),
            Amount = decimal.Round(request.Amount, 2),
            ReferenceNumber = string.IsNullOrWhiteSpace(request.ReferenceNumber) ? null : request.ReferenceNumber.Trim(),
            Remarks = request.Remarks.Trim(),
            CreatedAt = DateTime.UtcNow, CreatedBy = _current.UserId, Status = true
        };
        _db.CashBookEntries.Add(entry);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "CashBook", nameof(CashBookEntry), entry.Id.ToString(), newValue: entry);
        return Ok(new { message = "Other cash payment saved in Cash Book.", id = entry.Id });
    }
}
