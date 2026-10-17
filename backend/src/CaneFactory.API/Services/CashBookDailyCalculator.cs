using CaneFactory.Domain.Entities;

namespace CaneFactory.API.Services;

/// <summary>
/// Converts the immutable physical-cash ledger into day-wise reconciliation rows.
/// A day-closing withdrawal is deliberately kept as a normal CASH_OUT row so the
/// next day's opening always equals the actual cash left at the factory.
/// </summary>
public static class CashBookDailyCalculator
{
    public const string DayClosingWithdrawal = "DAY_CLOSING_WITHDRAWAL";
    public const string PaymentReversal = "PAYMENT_REVERSAL";

    /// <summary>
    /// Reversal rows remain visible in the immutable ledger, but must neutralize
    /// the original farmer cash payment in operational totals.  A reversal is
    /// not new physical cash received from a bank/party.
    /// </summary>
    public static decimal CashReceived(IEnumerable<CashBookEntry> entries) => entries
        .Where(x => x.EntryType == "CASH_IN" && x.SourceType != PaymentReversal)
        .Sum(x => x.Amount);

    public static decimal FarmerCashPaid(IEnumerable<CashBookEntry> entries) =>
        entries.Where(x => x.EntryType == "CASH_OUT" && x.SourceType == "FARMER_PAYMENT").Sum(x => x.Amount)
        - entries.Where(x => x.EntryType == "CASH_IN" && x.SourceType == PaymentReversal).Sum(x => x.Amount);

    public static decimal OtherCashPaid(IEnumerable<CashBookEntry> entries) => entries
        .Where(x => x.EntryType == "CASH_OUT" && x.SourceType != "FARMER_PAYMENT" && x.SourceType != DayClosingWithdrawal)
        .Sum(x => x.Amount);

    public static decimal ClosingWithdrawal(IEnumerable<CashBookEntry> entries) => entries
        .Where(x => x.EntryType == "CASH_OUT" && x.SourceType == DayClosingWithdrawal)
        .Sum(x => x.Amount);

    public static IReadOnlyList<CashBookDailySummary> Build(
        IEnumerable<CashBookEntry> source,
        decimal openingBalance,
        DateTime? requestedFrom = null,
        DateTime? requestedTo = null)
    {
        var entries = source.OrderBy(x => x.EntryDate).ThenBy(x => x.Id).ToList();
        var byDate = entries.GroupBy(x => x.EntryDate.Date)
            .ToDictionary(x => x.Key, x => x.ToList());
        var dates = byDate.Keys.OrderBy(x => x).ToList();

        // A single-day report must still show a zero-activity reconciliation row.
        if (requestedFrom.HasValue && requestedTo.HasValue &&
            requestedFrom.Value.Date == requestedTo.Value.Date &&
            !byDate.ContainsKey(requestedFrom.Value.Date))
            dates.Add(requestedFrom.Value.Date);

        dates.Sort();
        var runningOpening = openingBalance;
        var result = new List<CashBookDailySummary>(dates.Count);
        foreach (var date in dates)
        {
            var day = byDate.GetValueOrDefault(date) ?? [];
            var cashReceived = CashReceived(day);
            var farmerCashPaid = FarmerCashPaid(day);
            var closingWithdrawal = ClosingWithdrawal(day);
            var otherCashPaid = OtherCashPaid(day);
            var operatingCashPaid = farmerCashPaid + otherCashPaid;
            var availableBeforeClosing = runningOpening + cashReceived - operatingCashPaid;
            var closingBalance = availableBeforeClosing - closingWithdrawal;
            var closeEntry = day.LastOrDefault(x => x.EntryType == "CASH_OUT" &&
                x.SourceType == DayClosingWithdrawal);

            result.Add(new CashBookDailySummary(
                date, runningOpening, cashReceived, farmerCashPaid, otherCashPaid,
                operatingCashPaid, availableBeforeClosing, closingWithdrawal,
                closingBalance, closeEntry != null, closeEntry?.SourceName,
                closeEntry?.ReferenceNumber, closeEntry?.Remarks));
            runningOpening = closingBalance;
        }
        return result;
    }
}

public sealed record CashBookDailySummary(
    DateTime Date,
    decimal OpeningCash,
    decimal CashReceived,
    decimal FarmerCashPaid,
    decimal OtherCashPaid,
    decimal OperatingCashPaid,
    decimal AvailableBeforeClosing,
    decimal ClosingWithdrawal,
    decimal ClosingBalance,
    bool IsClosed,
    string? CashTakenBy,
    string? ReferenceNumber,
    string? Remarks);
