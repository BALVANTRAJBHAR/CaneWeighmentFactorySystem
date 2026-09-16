using CaneFactory.Domain.Common;

namespace CaneFactory.Domain.Entities;

/// <summary>
/// Immutable cash ledger. CASH_IN rows are entered by the operator; CASH_OUT rows are generated
/// automatically when a farmer payment is completed using the CASH payment mode.
/// </summary>
public class CashBookEntry : BaseEntity
{
    public DateTime EntryDate { get; set; }
    public string EntryType { get; set; } = "CASH_IN"; // CASH_IN | CASH_OUT
    public string SourceType { get; set; } = string.Empty; // BANK | PARTY | OTHER | FARMER_PAYMENT | PAYMENT_REVERSAL
    public string? SourceName { get; set; }
    public decimal Amount { get; set; }
    public int? PaymentId { get; set; }
    public int? GrowerId { get; set; }
    public string? GrowerCode { get; set; }
    public string? GrowerName { get; set; }
    public decimal? NetPayableAmount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Remarks { get; set; }
}
