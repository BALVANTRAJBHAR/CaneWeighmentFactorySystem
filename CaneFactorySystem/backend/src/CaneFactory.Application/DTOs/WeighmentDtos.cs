namespace CaneFactory.Application.DTOs;

public class GrossSaveRequest
{
    public string GrowerCode { get; set; } = string.Empty;
    public int VehicleTypeId { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public int VarietyTypeId { get; set; }
    public int VarietyId { get; set; }
    public decimal CuttingPercent { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal ScaleReadingKg { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class TareSaveRequest
{
    public int PurchaseId { get; set; }
    public decimal ScaleReadingKg { get; set; }
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
    public decimal TaxPercent { get; set; }
    public decimal? TaxWeightQuintal { get; set; }
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

public class SalePurchaseTareSaveRequest
{
    public int ItemId { get; set; }
    public int PartyId { get; set; }
    public int VehicleTypeId { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string? Remark { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class SalePurchaseGrossSaveRequest
{
    public int SalePurchaseId { get; set; }
    public decimal? Rate { get; set; }
    public string? IdempotencyKey { get; set; }
}
