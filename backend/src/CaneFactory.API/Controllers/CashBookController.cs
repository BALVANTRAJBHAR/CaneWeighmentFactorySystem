using CaneFactory.Application.Interfaces;
using CaneFactory.API.Services;
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

/// <summary>Closes one physical-cash day by withdrawing the complete cash-on-hand
/// calculated by the server. The amount is never accepted from the client.</summary>
public sealed class CashBookDayCloseRequest
{
    public DateTime? EntryDate { get; set; }
    public string? CashTakenBy { get; set; }
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
            var isGrowerId = int.TryParse(term, out var parsedGrowerId);
            q = q.Where(x => (x.SourceName != null && x.SourceName.Contains(term)) ||
                (isGrowerId && x.GrowerId == parsedGrowerId) ||
                (x.GrowerCode != null && x.GrowerCode.Contains(term)) ||
                (x.GrowerName != null && x.GrowerName.Contains(term)) ||
                (x.ReferenceNumber != null && x.ReferenceNumber.Contains(term)));
        }
        var received = await q.Where(x => x.EntryType == "CASH_IN").SumAsync(x => (decimal?)x.Amount) ?? 0;
        var paid = await q.Where(x => x.EntryType == "CASH_OUT").SumAsync(x => (decimal?)x.Amount) ?? 0;
        var farmerCashPaid = await q.Where(x => x.EntryType == "CASH_OUT" && x.SourceType == "FARMER_PAYMENT")
            .SumAsync(x => (decimal?)x.Amount) ?? 0;
        var otherCashPaid = await q.Where(x => x.EntryType == "CASH_OUT" &&
                x.SourceType != "FARMER_PAYMENT" &&
                x.SourceType != CashBookDailyCalculator.DayClosingWithdrawal)
            .SumAsync(x => (decimal?)x.Amount) ?? 0;
        var closingWithdrawal = await q.Where(x => x.EntryType == "CASH_OUT" &&
                x.SourceType == CashBookDailyCalculator.DayClosingWithdrawal)
            .SumAsync(x => (decimal?)x.Amount) ?? 0;
        var totalCount = await q.CountAsync();
        // Calculate the balance in chronological order, then display newest entries first.
        // This avoids a misleading balance when the visible table is sorted descending.
        var entries = await q.OrderBy(x => x.EntryDate).ThenBy(x => x.Id).ToListAsync();
        var dailySummaries = CashBookDailyCalculator.Build(entries, openingBalance, from, toDate?.Date);
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
            dailySummaries,
            totals = new { openingBalance, received, paid, farmerCashPaid, otherCashPaid,
                operatingCashPaid = paid - closingWithdrawal, closingWithdrawal,
                availableBeforeClosing = openingBalance + received - (paid - closingWithdrawal),
                closingBalance = openingBalance + received - paid }
        });
    }

    /// <summary>Records physical cash received from Bank, Party or another external source.</summary>
    [HttpPost("receipts")]
    public async Task<IActionResult> ReceiveCash([FromBody] CashBookReceiptRequest request)
    {
        if (Deny("Create") is { } denied) return denied;
        if (request.EntryDate.HasValue && request.EntryDate.Value.Date > DateTime.Today)
            return BadRequest(new { message = "Cash receipt date cannot be in the future." });
        var sourceType = (request.SourceType ?? string.Empty).Trim().ToUpperInvariant();
        if (sourceType is not ("BANK" or "PARTY" or "OTHER"))
            return BadRequest(new { message = "Source must be Bank, Party or Other." });
        if (string.IsNullOrWhiteSpace(request.SourceName))
            return BadRequest(new { message = "Source name is required." });
        if (request.Amount <= 0) return BadRequest(new { message = "Cash amount must be greater than zero." });
        if (request.Amount > 99_999_999.99m) return BadRequest(new { message = "Cash amount is too large." });

        var entryDate = (request.EntryDate ?? DateTime.Now).Date;
        if (await IsDayClosedAsync(entryDate))
            return Conflict(new { message = $"Cash Book for {entryDate:dd-MM-yyyy} is already closed. Add this receipt on the next working day." });

        var entry = new CashBookEntry
        {
            EntryDate = entryDate,
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
        if (request.EntryDate.HasValue && request.EntryDate.Value.Date > DateTime.Today)
            return BadRequest(new { message = "Cash payment date cannot be in the future." });
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

        var entryDate = (request.EntryDate ?? DateTime.Now).Date;
        if (await IsDayClosedAsync(entryDate))
            return Conflict(new { message = $"Cash Book for {entryDate:dd-MM-yyyy} is already closed. A new cash payment cannot be posted to a closed day." });

        var entry = new CashBookEntry
        {
            EntryDate = entryDate,
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

    /// <summary>
    /// Withdraws the complete calculated cash remaining at the end of a day. This is
    /// an immutable ledger entry, not a reset: the following day's opening therefore
    /// becomes zero (or any genuine amount still left after reconciliation).
    /// </summary>
    [HttpPost("day-close")]
    public async Task<IActionResult> CloseDay([FromBody] CashBookDayCloseRequest request)
    {
        if (Deny("Create") is { } denied) return denied;
        var entryDate = (request.EntryDate ?? DateTime.Now).Date;
        if (entryDate > DateTime.Today)
            return BadRequest(new { message = "Cash Book closing date cannot be in the future." });
        if (string.IsNullOrWhiteSpace(request.CashTakenBy))
            return BadRequest(new { message = "Enter the name of the person taking the closing cash." });
        if (request.CashTakenBy.Trim().Length > 150)
            return BadRequest(new { message = "Cash Taken By must be 150 characters or fewer." });
        if (request.Remarks?.Trim().Length > 500)
            return BadRequest(new { message = "Remarks must be 500 characters or fewer." });

        CashBookEntry? closingEntry = null;
        decimal amount = 0;
        IActionResult? validationError = null;
        await _db.ExecuteInTransactionAsync(async () =>
        {
            if (await IsDayClosedAsync(entryDate))
            {
                validationError = Conflict(new { message = $"Cash Book for {entryDate:dd-MM-yyyy} is already closed." });
                return;
            }

            // Do not rewrite historical opening balances after later business has
            // already been posted. A missed close must be reconciled by Admin first.
            if (await _db.CashBookEntries.AnyAsync(x => !x.IsDeleted && x.EntryDate > entryDate))
            {
                validationError = Conflict(new
                {
                    message = "This past day cannot be closed because later Cash Book entries already exist. Close the current day or ask Admin to reconcile the missed day."
                });
                return;
            }

            // Materialize the small two-column ledger projection before summing.
            // This works consistently on SQL Server and the SQLite regression suite,
            // whose provider cannot translate decimal SUM.
            var throughDay = await _db.CashBookEntries
                .Where(x => !x.IsDeleted && x.EntryDate <= entryDate)
                .Select(x => new { x.EntryType, x.Amount })
                .ToListAsync();
            var cashIn = throughDay.Where(x => x.EntryType == "CASH_IN").Sum(x => x.Amount);
            var cashOut = throughDay.Where(x => x.EntryType == "CASH_OUT").Sum(x => x.Amount);
            amount = decimal.Round(cashIn - cashOut, 2);
            if (amount < 0)
            {
                validationError = Conflict(new
                {
                    message = $"Cash Book is short by Rs {Math.Abs(amount):F2}. Reconcile the entries before closing the day."
                });
                return;
            }

            closingEntry = new CashBookEntry
            {
                EntryDate = entryDate,
                EntryType = "CASH_OUT",
                SourceType = CashBookDailyCalculator.DayClosingWithdrawal,
                SourceName = request.CashTakenBy.Trim(),
                Amount = amount,
                ReferenceNumber = string.IsNullOrWhiteSpace(request.ReferenceNumber)
                    ? $"DAY-CLOSE-{entryDate:yyyyMMdd}"
                    : request.ReferenceNumber.Trim(),
                Remarks = string.IsNullOrWhiteSpace(request.Remarks)
                    ? "End-of-day physical cash withdrawn"
                    : request.Remarks.Trim(),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _current.UserId,
                Status = true
            };
            _db.CashBookEntries.Add(closingEntry);
            await _db.SaveChangesAsync();
        });

        if (validationError != null) return validationError;
        await _audit.LogAsync("DayClose", "CashBook", nameof(CashBookEntry), closingEntry!.Id.ToString(),
            newValue: new { closingEntry.EntryDate, closingEntry.Amount, cashTakenBy = closingEntry.SourceName,
                closingEntry.ReferenceNumber, closingEntry.Remarks });
        return Ok(new
        {
            message = amount == 0
                ? "Day closed with no cash left to withdraw."
                : $"Day closed. Rs {amount:F2} recorded as closing cash withdrawal.",
            id = closingEntry.Id,
            entryDate,
            withdrawnAmount = amount,
            closingBalance = 0m
        });
    }

    [HttpGet("daily-summary")]
    public async Task<IActionResult> DailySummary([FromQuery] DateTime? date)
    {
        if (Deny("View") is { } denied) return denied;
        var selectedDate = (date ?? DateTime.Today).Date;
        if (selectedDate > DateTime.Today)
            return BadRequest(new { message = "Daily summary date cannot be in the future." });

        var baseQuery = _db.CashBookEntries.AsNoTracking().Where(x => !x.IsDeleted);
        var openingIn = await baseQuery.Where(x => x.EntryDate < selectedDate && x.EntryType == "CASH_IN")
            .SumAsync(x => (decimal?)x.Amount) ?? 0;
        var openingOut = await baseQuery.Where(x => x.EntryDate < selectedDate && x.EntryType == "CASH_OUT")
            .SumAsync(x => (decimal?)x.Amount) ?? 0;
        var dayEntries = await baseQuery.Where(x => x.EntryDate == selectedDate)
            .OrderBy(x => x.Id).ToListAsync();
        var summary = CashBookDailyCalculator.Build(dayEntries, openingIn - openingOut,
            selectedDate, selectedDate).Single();
        return Ok(summary);
    }

    private Task<bool> IsDayClosedAsync(DateTime entryDate) =>
        _db.CashBookEntries.AnyAsync(x => !x.IsDeleted && x.EntryDate == entryDate.Date &&
            x.EntryType == "CASH_OUT" && x.SourceType == CashBookDailyCalculator.DayClosingWithdrawal);
}
