using CaneFactory.Domain.Common;

namespace CaneFactory.Domain.Entities;

public class Payment : BaseEntity
{
    public int AdviceNumber { get; set; }
    public int GrowerId { get; set; }
    public Grower Grower { get; set; } = null!;
    public string GrowerCode { get; set; } = string.Empty;
    public int VillageId { get; set; }
    public decimal TotalPurchaseAmount { get; set; }
    public decimal LoanDeductedAmount { get; set; }
    public decimal NetPayableAmount { get; set; }
    public int PaymentModeId { get; set; }
    public PaymentModeMaster PaymentMode { get; set; } = null!;
    public string? TransactionRefNumber { get; set; }
    /// <summary>Shared opaque token for all independently auditable payments created by one
    /// date-range batch. It lets the client print one consolidated PDF without exposing IDs in a URL.</summary>
    public string? BatchPrintToken { get; set; }
    // Immutable recipient-bank snapshot for BANK payments. It keeps historical payment PDFs
    // accurate even if the grower's bank master/account details are edited later.
    public string? AccountHolderNameAtPayment { get; set; }
    public string? BankNameAtPayment { get; set; }
    public string? BankBranchAtPayment { get; set; }
    public string? BankIfscAtPayment { get; set; }
    public string? BankAccountNumberAtPayment { get; set; }
    public DateTime PaymentDate { get; set; }
    public int PaidByUserId { get; set; }
    public string PaidByUserName { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = "COMPLETED"; // COMPLETED | CANCELLED
    public string? CancelReason { get; set; }
    public int SeasonId { get; set; }
    public Season Season { get; set; } = null!;

    // Phase 7 print/reprint audit counter (0 = never printed; >0 subsequent prints are audited as Reprint)
    public int PrintCount { get; set; }
}

/// <summary>One row per Purchase included in a Payment/Advice batch - snapshots the amount at the
/// time of payment so later Purchase edits never retroactively change a completed Payment's totals.</summary>
public class PaymentPurchase
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;
    public int PurchaseId { get; set; }
    public Purchase Purchase { get; set; } = null!;
    public decimal PurchaseAmountAtPayment { get; set; }
}

/// <summary>Cash Evidence photo captured for a Payment (mirrors PurchaseImage - metadata + disk file
/// only, never a DB BLOB). Primarily used for CASH mode payments.</summary>
public class PaymentImage
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    /// <summary>The paid purchase this image proves. Null is retained only for legacy/uploaded evidence.</summary>
    public int? PurchaseId { get; set; }
    public int CameraId { get; set; }
    public string ImageName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public int? CapturedBy { get; set; }
    public bool Status { get; set; } = true;
}
