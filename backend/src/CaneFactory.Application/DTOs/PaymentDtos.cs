namespace CaneFactory.Application.DTOs;

public class PaymentCreateRequest
{
    public string SelectionMode { get; set; } = "SINGLE"; // SINGLE | DATE_RANGE | FARMER
    public string GrowerCode { get; set; } = string.Empty;
    public int? PurchaseId { get; set; } // required for SINGLE
    public DateTime? FromDate { get; set; } // used for DATE_RANGE
    public DateTime? ToDate { get; set; } // used for DATE_RANGE
    public int PaymentModeId { get; set; }
    public string? TransactionRefNumber { get; set; }
    public string? IdempotencyKey { get; set; }
}

/// <summary>Manual cash-payment evidence capture request. ReplaceExisting retains the historical audit row
/// but makes the new image the active evidence for the selected paid purchase.</summary>
public class PaymentEvidenceCaptureRequest
{
    public int? PurchaseId { get; set; }
    public bool ReplaceExisting { get; set; }
}
