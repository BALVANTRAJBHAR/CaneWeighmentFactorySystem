namespace CaneFactory.Application.DTOs;

public interface IScaleReadingProofRequest
{
    decimal ScaleReadingKg { get; set; }
    bool ScaleReadingStable { get; set; }
    DateTime? ScaleReadingCapturedAtUtc { get; set; }
    string? ScaleBridgeSessionId { get; set; }
    long ScaleBridgeZeroCounter { get; set; }
    string? ScaleReadingNonce { get; set; }
    string? ScaleReadingSignature { get; set; }
}

public class GrossSaveRequest : IScaleReadingProofRequest
{
    public int GrowerId { get; set; }
    // Kept temporarily so an older desktop client can finish an in-progress
    // rollout. New clients identify growers only by the six-digit GrowerId.
    public string GrowerCode { get; set; } = string.Empty;
    public int VehicleTypeId { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public int VarietyTypeId { get; set; }
    public int VarietyId { get; set; }
    public int? CropId { get; set; }
    public decimal CuttingPercent { get; set; }
    public decimal OtherDeductionPercent { get; set; }
    public decimal ScaleReadingKg { get; set; }
    public bool ScaleReadingStable { get; set; }
    public DateTime? ScaleReadingCapturedAtUtc { get; set; }
    public string? ScaleBridgeSessionId { get; set; }
    public long ScaleBridgeZeroCounter { get; set; }
    public string? ScaleReadingNonce { get; set; }
    public string? ScaleReadingSignature { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class TareSaveRequest : IScaleReadingProofRequest
{
    public int PurchaseId { get; set; }
    public decimal ScaleReadingKg { get; set; }
    public bool ScaleReadingStable { get; set; }
    public DateTime? ScaleReadingCapturedAtUtc { get; set; }
    public string? ScaleBridgeSessionId { get; set; }
    public long ScaleBridgeZeroCounter { get; set; }
    public string? ScaleReadingNonce { get; set; }
    public string? ScaleReadingSignature { get; set; }
    public decimal? Rate { get; set; }
    public int? ApprovedByUserId { get; set; }
    public int? RateReasonId { get; set; }
    public string? RateOverrideRemark { get; set; }
    public string? RateEvidenceToken { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class PurchaseDto
{
    public int Id { get; set; }
    public string GrowerCode { get; set; } = string.Empty;
    public string GrowerName { get; set; } = string.Empty;
    public string FatherName { get; set; } = string.Empty;
    public string VillageName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public string VehicleTypeName { get; set; } = string.Empty;
    public string VarietyName { get; set; } = string.Empty;
    public string VarietyTypeName { get; set; } = string.Empty;
    public decimal GrossWeightQuintal { get; set; }
    public DateTime GrossDateTime { get; set; }
    public string GrossByUserName { get; set; } = string.Empty;
    public decimal? TareWeightQuintal { get; set; }
    public DateTime? TareDateTime { get; set; }
    public string? TareByUserName { get; set; }
    public decimal? NetWeightQuintal { get; set; }
    public decimal CuttingPercent { get; set; }
    public decimal? CuttingWeightQuintal { get; set; }
    public decimal OtherDeductionPercent { get; set; }
    public decimal? OtherDeductionWeightQuintal { get; set; }
    public decimal? FinalWeightQuintal { get; set; }
    public decimal Rate { get; set; }
    public decimal? PurchaseAmount { get; set; }
    public string GrossTareStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string LockStatus { get; set; } = string.Empty;
    public int GrossPrintCount { get; set; }
    public int TarePrintCount { get; set; }
}

public class GrowerLookupDto
{
    public int GrowerId { get; set; }
    public string GrowerCode { get; set; } = string.Empty;
    public string GrowerName { get; set; } = string.Empty;
    public string FatherName { get; set; } = string.Empty;
    public string VillageName { get; set; } = string.Empty;
    public int VillageId { get; set; }
    public string? BankName { get; set; }
    public string? AccountMasked { get; set; }
    public string Mobile { get; set; } = string.Empty;
}

public class ParserTestRequest
{
    public int? StringProfileId { get; set; }
    public string? RawHex { get; set; }
    public string? RawText { get; set; }
}

public class ParsedFrameDto
{
    public bool FrameValid { get; set; }
    public string? Sign { get; set; }
    public string? WeightCharacters { get; set; }
    public decimal? NumericWeight { get; set; }
    public string? WeightUnit { get; set; }
    public string? RawHex { get; set; }
    public string? Error { get; set; }
}

public class LiveWeightDto
{
    public decimal WeightKg { get; set; }
    public decimal WeightQuintal { get; set; }
    public string WeightUnit { get; set; } = "KG";
    public bool Stable { get; set; }
    public bool DeviceConnected { get; set; }
    public bool ReaderRunning { get; set; }
    /// <summary>True only while a connected reader/simulator has supplied a current frame.</summary>
    public bool IsLive { get; set; }
    public string ReaderState { get; set; } = "DISCONNECTED";
    public DateTime LastReceivedAt { get; set; }
    public string? DeviceName { get; set; }
    public string? Error { get; set; }
}

public class SalePurchaseTareSaveRequest : IScaleReadingProofRequest
{
    public int ItemId { get; set; }
    public int PartyId { get; set; }
    public int VehicleTypeId { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string? Remark { get; set; }
    /// <summary>Operator-selected supply classification, confirmed again at gross.</summary>
    public string TaxSupplyType { get; set; } = "INTRA_STATE";
    public decimal ScaleReadingKg { get; set; }
    public bool ScaleReadingStable { get; set; }
    public DateTime? ScaleReadingCapturedAtUtc { get; set; }
    public string? ScaleBridgeSessionId { get; set; }
    public long ScaleBridgeZeroCounter { get; set; }
    public string? ScaleReadingNonce { get; set; }
    public string? ScaleReadingSignature { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class SalePurchaseGrossSaveRequest : IScaleReadingProofRequest
{
    public int SalePurchaseId { get; set; }
    public decimal ScaleReadingKg { get; set; }
    public bool ScaleReadingStable { get; set; }
    public DateTime? ScaleReadingCapturedAtUtc { get; set; }
    public string? ScaleBridgeSessionId { get; set; }
    public long ScaleBridgeZeroCounter { get; set; }
    public string? ScaleReadingNonce { get; set; }
    public string? ScaleReadingSignature { get; set; }
    public decimal? Rate { get; set; }
    public decimal GstPercent { get; set; }
    public decimal SalesPercent { get; set; }
    /// <summary>INTRA_STATE (CGST + SGST) or INTER_STATE (IGST).</summary>
    public string TaxSupplyType { get; set; } = "INTRA_STATE";
    public int? ApprovedByUserId { get; set; }
    public int? RateReasonId { get; set; }
    public string? RateOverrideRemark { get; set; }
    public string? RateEvidenceToken { get; set; }
    public string? IdempotencyKey { get; set; }
}
