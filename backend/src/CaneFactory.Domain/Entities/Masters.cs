using CaneFactory.Domain.Common;

namespace CaneFactory.Domain.Entities;

public class Zone : BaseEntity
{
    public string ZoneCode { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public string? ZoneNameHi { get; set; }
    public string? Description { get; set; }
}

public class Village : BaseEntity
{
    public int ZoneId { get; set; }
    public Zone Zone { get; set; } = null!;
    public string VillageName { get; set; } = string.Empty;
    public string? VillageNameHi { get; set; }
    public string? PradhanName { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
}

public class Bank : BaseEntity
{
    public string BankName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string IFSC { get; set; } = string.Empty;
    public string? ManagerName { get; set; }
    public string? ManagerMobile { get; set; }
    public string? ManagerEmail { get; set; }
}

public class Grower : BaseEntity
{
    public int VillageId { get; set; }
    public Village Village { get; set; } = null!;
    public int GrowerSequence { get; set; }
    public string GrowerCode { get; set; } = string.Empty;
    public string GrowerName { get; set; } = string.Empty;
    public string? GrowerNameHi { get; set; }
    public string FatherName { get; set; } = string.Empty;
    public string? FatherNameHi { get; set; }
    public int? BankId { get; set; }
    public Bank? Bank { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? AccountHolderName { get; set; }
    public string? AadhaarEncrypted { get; set; }
    public string? AadhaarLast4 { get; set; }
    public string? AadhaarHash { get; set; }
    public string Mobile { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public class VehicleType : BaseEntity
{
    public string VehicleTypeName { get; set; } = string.Empty;
    public string? VehicleTypeNameHi { get; set; }
}

public class VarietyType : BaseEntity
{
    public string VarietyTypeName { get; set; } = string.Empty;
}

public class Variety : BaseEntity
{
    public int VarietyTypeId { get; set; }
    public VarietyType VarietyType { get; set; } = null!;
    public string VarietyName { get; set; } = string.Empty;
}

public class RateMaster : BaseEntity
{
    public int VarietyTypeId { get; set; }
    public VarietyType VarietyType { get; set; } = null!;
    public decimal Rate { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}

/// <summary>Effective-dated non-cane sale/purchase rate.  Rates are immutable history:
/// a correction is entered as a new period rather than overwriting a completed weighment.</summary>
public class SaleItemRateMaster : BaseEntity
{
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public decimal Rate { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}

public class Item : BaseEntity
{
    public string ItemName { get; set; } = string.Empty;
    public string? ItemNameHi { get; set; }
}

public class Party : BaseEntity
{
    public string PartyName { get; set; } = string.Empty;
    public string? PartyNameHi { get; set; }
    public string Mobile { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Gst { get; set; }
}

public class Season : BaseEntity
{
    public string SeasonName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
}

public class PaymentModeMaster : BaseEntity
{
    public string ModeCode { get; set; } = string.Empty;
    public string ModeName { get; set; } = string.Empty;
}

public class CompanyConfig : BaseEntity
{
    public string CompanyName { get; set; } = string.Empty;
    public string? CompanyNameHi { get; set; }
    public string? Address { get; set; }
    public string? AddressHi { get; set; }
    public string? LogoPath { get; set; }
    public string DefaultLanguage { get; set; } = "en";
    public string? ThemeColor { get; set; }
}
