using CaneFactory.Application.Common;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

[Route("api/zones")]
public class ZonesController : MasterControllerBase<Zone>
{
    public ZonesController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq)
        : base(db, audit, current, seq) { }

    protected override string Module => "Zone";
    protected override bool UseSequenceId => true;
    protected override string SequenceName => "ZoneId";
    protected override long SequenceStart => 1;
    protected override string DisplayName(Zone e) => e.ZoneName;
    protected override System.Linq.Expressions.Expression<Func<Zone, object>> DisplayNameExpr() => e => e.ZoneName;
    protected override IQueryable<Zone> ApplySearch(IQueryable<Zone> q, string s) =>
        q.Where(z => z.ZoneName.Contains(s) || z.ZoneCode.Contains(s));

    protected override async Task<string?> ValidateAsync(Zone e, int? id)
    {
        e.ZoneName = Validators.Norm(e.ZoneName);
        if (e.ZoneName.Length == 0) return "Zone Name is required.";
        if (e.ZoneName.Length > 100) return "Zone Name must be at most 100 characters.";
        if (await Db.Zones.AnyAsync(z => !z.IsDeleted && z.Id != id && z.ZoneName.ToLower() == e.ZoneName.ToLower()))
            return $"Zone '{e.ZoneName}' already exists.";
        return null;
    }

    protected override void ApplyUpdate(Zone t, Zone s)
    {
        t.ZoneCode = s.ZoneCode; t.ZoneName = s.ZoneName; t.Description = s.Description;
    }

    protected override async Task<string?> InUseReasonAsync(int id) =>
        await Db.Villages.AnyAsync(v => v.ZoneId == id && !v.IsDeleted)
            ? "This Zone has Villages linked to it and cannot be deleted." : null;
}

[Route("api/villages")]
public class VillagesController : MasterControllerBase<Village>
{
    public VillagesController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq)
        : base(db, audit, current, seq) { }

    protected override string Module => "Village";
    protected override bool UseSequenceId => true;
    protected override string SequenceName => "VillageId";
    protected override long SequenceStart => 101;
    protected override string DisplayName(Village e) => e.VillageName;
    protected override System.Linq.Expressions.Expression<Func<Village, object>> DisplayNameExpr() => e => e.VillageName;
    protected override IQueryable<Village> ApplySearch(IQueryable<Village> q, string s) =>
        q.Where(v => v.VillageName.Contains(s) || (v.PradhanName != null && v.PradhanName.Contains(s)));

    protected override async Task<object> ProjectListAsync(IQueryable<Village> q) =>
        await q.Select(v => new
        {
            v.Id, v.ZoneId, ZoneName = v.Zone.ZoneName, v.VillageName, v.PradhanName,
            v.Mobile, v.Email, v.Status, v.CreatedAt
        }).ToListAsync();

    protected override async Task<string?> ValidateAsync(Village e, int? id)
    {
        e.VillageName = Validators.Norm(e.VillageName);
        if (e.VillageName.Length == 0) return "Village Name is required.";
        if (!await Db.Zones.AnyAsync(z => z.Id == e.ZoneId && !z.IsDeleted && z.Status))
            return "Selected Zone does not exist or is inactive.";
        if (!string.IsNullOrEmpty(e.Mobile) && !Validators.IsMobile(e.Mobile))
            return "Mobile must be exactly 10 numeric digits. Example: 9876543210";
        if (!Validators.IsEmail(e.Email)) return "Email format is invalid. Example: pradhan@gmail.com";
        if (await Db.Villages.AnyAsync(v => !v.IsDeleted && v.Id != id && v.ZoneId == e.ZoneId
                && v.VillageName.ToLower() == e.VillageName.ToLower()))
            return $"Village '{e.VillageName}' already exists in this Zone.";
        return null;
    }

    protected override void ApplyUpdate(Village t, Village s)
    {
        t.ZoneId = s.ZoneId; t.VillageName = s.VillageName; t.PradhanName = s.PradhanName;
        t.Mobile = s.Mobile; t.Email = s.Email;
    }

    protected override async Task<string?> InUseReasonAsync(int id) =>
        await Db.Growers.AnyAsync(g => g.VillageId == id && !g.IsDeleted)
            ? "This Village has Growers linked to it and cannot be deleted." : null;
}

[Route("api/banks")]
public class BanksController : MasterControllerBase<Bank>
{
    public BanksController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq)
        : base(db, audit, current, seq) { }

    protected override string Module => "Bank";
    protected override string DisplayName(Bank e) => $"{e.BankName} - {e.BranchName}";
    protected override System.Linq.Expressions.Expression<Func<Bank, object>> DisplayNameExpr() => e => e.BankName;
    protected override IQueryable<Bank> ApplySearch(IQueryable<Bank> q, string s) =>
        q.Where(b => b.BankName.Contains(s) || b.BranchName.Contains(s) || b.IFSC.Contains(s));

    protected override async Task<string?> ValidateAsync(Bank e, int? id)
    {
        e.BankName = Validators.Norm(e.BankName);
        e.BranchName = Validators.Norm(e.BranchName);
        e.IFSC = Validators.NormUpper(e.IFSC);
        if (e.BankName.Length == 0) return "Bank Name is required.";
        if (e.BranchName.Length == 0) return "Branch Name is required.";
        if (!Validators.IsIfsc(e.IFSC)) return "IFSC format is invalid. Example: SBIN0001234";
        if (!string.IsNullOrEmpty(e.ManagerMobile) && !Validators.IsMobile(e.ManagerMobile))
            return "Manager Mobile must be exactly 10 numeric digits.";
        if (!Validators.IsEmail(e.ManagerEmail)) return "Manager Email format is invalid.";
        if (await Db.Banks.AnyAsync(b => !b.IsDeleted && b.Id != id
                && b.BankName.ToLower() == e.BankName.ToLower() && b.BranchName.ToLower() == e.BranchName.ToLower()))
            return $"Bank '{e.BankName}' with branch '{e.BranchName}' already exists.";
        if (await Db.Banks.AnyAsync(b => !b.IsDeleted && b.Id != id && b.IFSC == e.IFSC))
            return $"IFSC '{e.IFSC}' is already registered for another branch.";
        return null;
    }

    protected override void ApplyUpdate(Bank t, Bank s)
    {
        t.BankName = s.BankName; t.BranchName = s.BranchName; t.Address = s.Address; t.IFSC = s.IFSC;
        t.ManagerName = s.ManagerName; t.ManagerMobile = s.ManagerMobile; t.ManagerEmail = s.ManagerEmail;
    }

    protected override async Task<string?> InUseReasonAsync(int id) =>
        await Db.Growers.AnyAsync(g => g.BankId == id && !g.IsDeleted)
            ? "This Bank is linked to Growers and cannot be deleted." : null;
}

[Route("api/vehicle-types")]
public class VehicleTypesController : MasterControllerBase<VehicleType>
{
    public VehicleTypesController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq)
        : base(db, audit, current, seq) { }

    protected override string Module => "Vehicle";
    protected override string DisplayName(VehicleType e) => e.VehicleTypeName;
    protected override System.Linq.Expressions.Expression<Func<VehicleType, object>> DisplayNameExpr() => e => e.VehicleTypeName;
    protected override IQueryable<VehicleType> ApplySearch(IQueryable<VehicleType> q, string s) =>
        q.Where(v => v.VehicleTypeName.Contains(s));

    protected override async Task<string?> ValidateAsync(VehicleType e, int? id)
    {
        e.VehicleTypeName = Validators.Norm(e.VehicleTypeName);
        if (e.VehicleTypeName.Length == 0) return "Vehicle Type Name is required.";
        if (await Db.VehicleTypes.AnyAsync(v => !v.IsDeleted && v.Id != id
                && v.VehicleTypeName.ToLower() == e.VehicleTypeName.ToLower()))
            return $"Vehicle Type '{e.VehicleTypeName}' already exists.";
        return null;
    }

    protected override void ApplyUpdate(VehicleType t, VehicleType s) => t.VehicleTypeName = s.VehicleTypeName;

    protected override async Task<string?> InUseReasonAsync(int id) =>
        await Db.Purchases.AnyAsync(p => p.VehicleTypeId == id)
            ? "This Vehicle Type is used in Purchases and cannot be deleted." : null;
}

[Route("api/variety-types")]
public class VarietyTypesController : MasterControllerBase<VarietyType>
{
    public VarietyTypesController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq)
        : base(db, audit, current, seq) { }

    protected override string Module => "VarietyType";
    protected override string DisplayName(VarietyType e) => e.VarietyTypeName;
    protected override System.Linq.Expressions.Expression<Func<VarietyType, object>> DisplayNameExpr() => e => e.VarietyTypeName;
    protected override IQueryable<VarietyType> ApplySearch(IQueryable<VarietyType> q, string s) =>
        q.Where(v => v.VarietyTypeName.Contains(s));

    protected override async Task<string?> ValidateAsync(VarietyType e, int? id)
    {
        e.VarietyTypeName = Validators.Norm(e.VarietyTypeName);
        if (e.VarietyTypeName.Length == 0) return "Variety Type Name is required.";
        if (await Db.VarietyTypes.AnyAsync(v => !v.IsDeleted && v.Id != id
                && v.VarietyTypeName.ToLower() == e.VarietyTypeName.ToLower()))
            return $"Variety Type '{e.VarietyTypeName}' already exists.";
        return null;
    }

    protected override void ApplyUpdate(VarietyType t, VarietyType s) => t.VarietyTypeName = s.VarietyTypeName;

    protected override async Task<string?> InUseReasonAsync(int id) =>
        await Db.Varieties.AnyAsync(v => v.VarietyTypeId == id && !v.IsDeleted)
            ? "This Variety Type has Varieties linked to it and cannot be deleted." : null;
}

[Route("api/varieties")]
public class VarietiesController : MasterControllerBase<Variety>
{
    public VarietiesController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq)
        : base(db, audit, current, seq) { }

    protected override string Module => "Variety";
    protected override string DisplayName(Variety e) => e.VarietyName;
    protected override System.Linq.Expressions.Expression<Func<Variety, object>> DisplayNameExpr() => e => e.VarietyName;
    protected override IQueryable<Variety> ApplySearch(IQueryable<Variety> q, string s) =>
        q.Where(v => v.VarietyName.Contains(s));

    protected override async Task<object> ProjectListAsync(IQueryable<Variety> q) =>
        await q.Select(v => new
        {
            v.Id, v.VarietyTypeId, VarietyTypeName = v.VarietyType.VarietyTypeName,
            v.VarietyName, v.Status, v.CreatedAt
        }).ToListAsync();

    /// <summary>Dynamic cascade: only the varieties of the selected VarietyType.</summary>
    [HttpGet("by-type/{varietyTypeId:int}")]
    public async Task<IActionResult> ByType(int varietyTypeId)
    {
        if (Deny("View") is { } d) return d;
        return Ok(await Db.Varieties
            .Where(v => v.VarietyTypeId == varietyTypeId && !v.IsDeleted && v.Status)
            .OrderBy(v => v.VarietyName)
            .Select(v => new { v.Id, v.VarietyName }).ToListAsync());
    }

    protected override async Task<string?> ValidateAsync(Variety e, int? id)
    {
        e.VarietyName = Validators.Norm(e.VarietyName);
        if (e.VarietyName.Length == 0) return "Variety Name is required.";
        if (!await Db.VarietyTypes.AnyAsync(v => v.Id == e.VarietyTypeId && !v.IsDeleted && v.Status))
            return "Selected Variety Type does not exist or is inactive.";
        if (await Db.Varieties.AnyAsync(v => !v.IsDeleted && v.Id != id && v.VarietyTypeId == e.VarietyTypeId
                && v.VarietyName.ToLower() == e.VarietyName.ToLower()))
            return $"Variety '{e.VarietyName}' already exists in this Variety Type.";
        return null;
    }

    protected override void ApplyUpdate(Variety t, Variety s)
    {
        t.VarietyTypeId = s.VarietyTypeId; t.VarietyName = s.VarietyName;
    }

    protected override async Task<string?> InUseReasonAsync(int id) =>
        await Db.Purchases.AnyAsync(p => p.VarietyId == id)
            ? "This Variety is used in Purchases and cannot be deleted." : null;
}

[Route("api/items")]
public class ItemsController : MasterControllerBase<Item>
{
    public ItemsController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq)
        : base(db, audit, current, seq) { }

    protected override string Module => "Item";
    protected override string DisplayName(Item e) => e.ItemName;
    protected override System.Linq.Expressions.Expression<Func<Item, object>> DisplayNameExpr() => e => e.ItemName;
    protected override IQueryable<Item> ApplySearch(IQueryable<Item> q, string s) => q.Where(i => i.ItemName.Contains(s));

    protected override async Task<string?> ValidateAsync(Item e, int? id)
    {
        e.ItemName = Validators.Norm(e.ItemName);
        if (e.ItemName.Length == 0) return "Item Name is required.";
        if (await Db.Items.AnyAsync(i => !i.IsDeleted && i.Id != id && i.ItemName.ToLower() == e.ItemName.ToLower()))
            return $"Item '{e.ItemName}' already exists.";
        return null;
    }

    protected override void ApplyUpdate(Item t, Item s) => t.ItemName = s.ItemName;
}

[Route("api/parties")]
public class PartiesController : MasterControllerBase<Party>
{
    public PartiesController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq)
        : base(db, audit, current, seq) { }

    protected override string Module => "Party";
    protected override string DisplayName(Party e) => e.PartyName;
    protected override System.Linq.Expressions.Expression<Func<Party, object>> DisplayNameExpr() => e => e.PartyName;
    protected override IQueryable<Party> ApplySearch(IQueryable<Party> q, string s) =>
        q.Where(p => p.PartyName.Contains(s) || p.Mobile.Contains(s));

    protected override async Task<string?> ValidateAsync(Party e, int? id)
    {
        e.PartyName = Validators.Norm(e.PartyName);
        if (e.PartyName.Length == 0) return "Party Name is required.";
        if (!Validators.IsMobile(e.Mobile)) return "Mobile must be exactly 10 numeric digits. Example: 9876543210";
        if (!Validators.IsEmail(e.Email)) return "Email format is invalid.";
        // strong duplicate block: same name + same mobile already exists
        if (await Db.Parties.AnyAsync(p => !p.IsDeleted && p.Id != id
                && p.PartyName.ToLower() == e.PartyName.ToLower() && p.Mobile == e.Mobile))
            return $"DUPLICATE: A Party with name '{e.PartyName}' AND mobile '{e.Mobile}' already exists. Saving blocked.";
        return null;
    }

    protected override void ApplyUpdate(Party t, Party s)
    {
        t.PartyName = s.PartyName; t.Mobile = s.Mobile; t.Email = s.Email; t.Address = s.Address; t.Gst = s.Gst;
    }

    /// <summary>Client-side duplicate warning support (name OR mobile matches).</summary>
    [HttpGet("duplicate-check")]
    public async Task<IActionResult> DuplicateCheck([FromQuery] string? name, [FromQuery] string? mobile, [FromQuery] int? excludeId)
    {
        if (Deny("View") is { } d) return d;
        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(name) && await Db.Parties.AnyAsync(p => !p.IsDeleted && p.Id != excludeId && p.PartyName.ToLower() == name.Trim().ToLower()))
            warnings.Add($"A Party named '{name.Trim()}' already exists.");
        if (!string.IsNullOrWhiteSpace(mobile) && await Db.Parties.AnyAsync(p => !p.IsDeleted && p.Id != excludeId && p.Mobile == mobile))
            warnings.Add($"Mobile '{mobile}' is already used by another Party.");
        return Ok(new { warnings });
    }
}

[Route("api/seasons")]
public class SeasonsController : MasterControllerBase<Season>
{
    public SeasonsController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq)
        : base(db, audit, current, seq) { }

    protected override string Module => "Season";
    protected override string DisplayName(Season e) => e.SeasonName;
    protected override System.Linq.Expressions.Expression<Func<Season, object>> DisplayNameExpr() => e => e.SeasonName;
    protected override IQueryable<Season> ApplySearch(IQueryable<Season> q, string s) => q.Where(x => x.SeasonName.Contains(s));

    protected override async Task<string?> ValidateAsync(Season e, int? id)
    {
        e.SeasonName = Validators.Norm(e.SeasonName);
        if (e.SeasonName.Length == 0) return "Season Name is required. Example: 2026-27";
        if (await Db.Seasons.AnyAsync(x => !x.IsDeleted && x.Id != id && x.SeasonName == e.SeasonName))
            return $"Season '{e.SeasonName}' already exists.";
        return null;
    }

    public override async Task<IActionResult> Create([FromBody] Season entity)
    {
        var result = await base.Create(entity);
        if (entity.IsActive && entity.Id > 0) await DeactivateOthersAsync(entity.Id);
        return result;
    }

    protected override void ApplyUpdate(Season t, Season s)
    {
        t.SeasonName = s.SeasonName; t.StartDate = s.StartDate; t.EndDate = s.EndDate; t.IsActive = s.IsActive;
        if (s.IsActive) DeactivateOthersAsync(t.Id).GetAwaiter().GetResult();
    }

    private async Task DeactivateOthersAsync(int keepId)
    {
        var others = await Db.Seasons.Where(x => x.Id != keepId && x.IsActive).ToListAsync();
        foreach (var o in others) o.IsActive = false;
        await Db.SaveChangesAsync();
    }
}

[Route("api/payment-modes")]
public class PaymentModesController : MasterControllerBase<PaymentModeMaster>
{
    public PaymentModesController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq)
        : base(db, audit, current, seq) { }

    protected override string Module => "PaymentMode";
    protected override string DisplayName(PaymentModeMaster e) => e.ModeName;
    protected override IQueryable<PaymentModeMaster> ApplySearch(IQueryable<PaymentModeMaster> q, string s) =>
        q.Where(m => m.ModeName.Contains(s) || m.ModeCode.Contains(s));

    protected override async Task<string?> ValidateAsync(PaymentModeMaster e, int? id)
    {
        e.ModeCode = Validators.NormUpper(e.ModeCode);
        e.ModeName = Validators.Norm(e.ModeName);
        if (e.ModeCode.Length == 0 || e.ModeName.Length == 0) return "Mode Code and Mode Name are required.";
        if (await Db.PaymentModes.AnyAsync(m => !m.IsDeleted && m.Id != id && m.ModeCode == e.ModeCode))
            return $"Payment Mode '{e.ModeCode}' already exists.";
        return null;
    }

    protected override void ApplyUpdate(PaymentModeMaster t, PaymentModeMaster s)
    {
        t.ModeCode = s.ModeCode; t.ModeName = s.ModeName;
    }
}
