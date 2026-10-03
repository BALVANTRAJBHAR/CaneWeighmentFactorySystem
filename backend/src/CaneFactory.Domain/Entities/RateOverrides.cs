using CaneFactory.Domain.Common;

namespace CaneFactory.Domain.Entities;

/// <summary>Operator-selectable reason for using a rate below the active master rate.</summary>
public class RateReasonMaster : BaseEntity
{
    public string ReasonName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// Sanitised, short-lived evidence uploaded before a final weighment save.  The opaque token
/// is consumed exactly once when the related rate override is committed.
/// </summary>
public class RateOverrideEvidence : BaseEntity
{
    public string Token { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public int TransactionId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ImageName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

/// <summary>Immutable financial/audit record for a master-rate reduction at final weighment.</summary>
public class WeighmentRateOverride : BaseEntity
{
    public string TransactionType { get; set; } = string.Empty;
    public int TransactionId { get; set; }
    public decimal MasterRate { get; set; }
    public decimal ApprovedRate { get; set; }
    public int ApprovedByUserId { get; set; }
    public string ApprovedByUserName { get; set; } = string.Empty;
    public int RateReasonId { get; set; }
    public string RateReasonText { get; set; } = string.Empty;
    public string? Remark { get; set; }
    public int EvidenceId { get; set; }
    public RateOverrideEvidence Evidence { get; set; } = null!;
}
