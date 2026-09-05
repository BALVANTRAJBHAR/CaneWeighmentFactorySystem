using CaneFactory.Domain.Common;

namespace CaneFactory.Domain.Entities;

public class Purchase : BaseEntity
{
    public int GrowerId { get; set; }
    public Grower Grower { get; set; } = null!;
    public int VillageId { get; set; }
    public string GrowerCode { get; set; } = string.Empty;
    public int VehicleTypeId { get; set; }
    public VehicleType VehicleType { get; set; } = null!;
    public string VehicleNumber { get; set; } = string.Empty;
    public int VarietyTypeId { get; set; }
    public int VarietyId { get; set; }
    public Variety Variety { get; set; } = null!;

    public decimal ScaleReadingGrossKg { get; set; }
    public decimal GrossWeightQuintal { get; set; }
    public DateTime GrossDateTime { get; set; }
    public int GrossByUserId { get; set; }
    public string GrossByUserName { get; set; } = string.Empty;

    public decimal? ScaleReadingTareKg { get; set; }
    public decimal? TareWeightQuintal { get; set; }
    public DateTime? TareDateTime { get; set; }
    public int? TareByUserId { get; set; }
    public string? TareByUserName { get; set; }

    public decimal? NetWeightQuintal { get; set; }
    public decimal CuttingPercent { get; set; }
    public decimal? CuttingWeightQuintal { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal? TaxWeightQuintal { get; set; }
    public decimal? FinalWeightQuintal { get; set; }

    public decimal Rate { get; set; }
    public decimal? PurchaseAmount { get; set; }

    public string PaymentFlag { get; set; } = "N";
    public string PaymentStatus { get; set; } = "NOT_ELIGIBLE";
    public string GrossTareStatus { get; set; } = "GROSS_DONE";
    public string LockStatus { get; set; } = "UNLOCKED";
    public int? AdviceNumber { get; set; }
    public int SeasonId { get; set; }
    public Season Season { get; set; } = null!;

    // Phase 7: print/reprint audit counters (0 = never printed; >0 subsequent prints are audited as Reprint)
    public int GrossPrintCount { get; set; }
    public int TarePrintCount { get; set; }
}

public class PurchaseImage
{
    public int Id { get; set; }
    public int PurchaseId { get; set; }
    public int GrowerId { get; set; }
    public int VillageId { get; set; }
    public int CameraId { get; set; }
    public string CaptureStage { get; set; } = string.Empty;
    public string ImageName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public int? CapturedBy { get; set; }
    public bool Status { get; set; } = true;
}

/// <summary>
/// Non-cane sale/purchase weighment. This is deliberately independent from Purchase and Payment:
/// it has its own lifecycle (TARE_PENDING_GROSS -> COMPLETED/CANCELLED), identifiers and audit trail.
/// </summary>
public class SalePurchase : BaseEntity
{
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int PartyId { get; set; }
    public Party Party { get; set; } = null!;
    public int VehicleTypeId { get; set; }
    public VehicleType VehicleType { get; set; } = null!;
    public string VehicleNumber { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string? Remark { get; set; }

    public decimal ScaleReadingTareKg { get; set; }
    public decimal TareWeightQuintal { get; set; }
    public DateTime TareDateTime { get; set; }
    public int TareByUserId { get; set; }
    public string TareByUserName { get; set; } = string.Empty;

    public decimal? ScaleReadingGrossKg { get; set; }
    public decimal? GrossWeightQuintal { get; set; }
    public decimal? FinalWeightQuintal { get; set; }
    public DateTime? GrossDateTime { get; set; }
    public int? GrossByUserId { get; set; }
    public string? GrossByUserName { get; set; }

    public decimal? Rate { get; set; }
    public decimal? Amount { get; set; }
    public string WeighmentStatus { get; set; } = "TARE_PENDING_GROSS";
    public string? CancelReason { get; set; }
    public int TarePrintCount { get; set; }
    public int GrossPrintCount { get; set; }
}

public class SalePurchaseImage
{
    public int Id { get; set; }
    public int SalePurchaseId { get; set; }
    public int CameraId { get; set; }
    public string CaptureStage { get; set; } = string.Empty;
    public string ImageName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public int? CapturedBy { get; set; }
    public bool Status { get; set; } = true;
}
