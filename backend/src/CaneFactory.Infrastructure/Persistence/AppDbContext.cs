using CaneFactory.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserOtp> UserOtps => Set<UserOtp>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Village> Villages => Set<Village>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<Grower> Growers => Set<Grower>();
    public DbSet<VehicleType> VehicleTypes => Set<VehicleType>();
    public DbSet<VarietyType> VarietyTypes => Set<VarietyType>();
    public DbSet<Variety> Varieties => Set<Variety>();
    public DbSet<RateMaster> Rates => Set<RateMaster>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<PaymentModeMaster> PaymentModes => Set<PaymentModeMaster>();
    public DbSet<CompanyConfig> CompanyConfigs => Set<CompanyConfig>();
    public DbSet<ExpenseType> ExpenseTypes => Set<ExpenseType>();
    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseImage> PurchaseImages => Set<PurchaseImage>();
    public DbSet<SalePurchase> SalePurchases => Set<SalePurchase>();
    public DbSet<SalePurchaseImage> SalePurchaseImages => Set<SalePurchaseImage>();

    public DbSet<LoanTypeMaster> LoanTypes => Set<LoanTypeMaster>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<LoanRecovery> LoanRecoveries => Set<LoanRecovery>();

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentPurchase> PaymentPurchases => Set<PaymentPurchase>();
    public DbSet<PaymentImage> PaymentImages => Set<PaymentImage>();
    public DbSet<CashBookEntry> CashBookEntries => Set<CashBookEntry>();

    public DbSet<WeighingDevice> WeighingDevices => Set<WeighingDevice>();
    public DbSet<StringProfile> StringProfiles => Set<StringProfile>();
    public DbSet<DeviceConfigHistory> DeviceConfigHistories => Set<DeviceConfigHistory>();
    public DbSet<WeightRuleConfig> WeightRules => Set<WeightRuleConfig>();
    public DbSet<SoundConfig> SoundConfigs => Set<SoundConfig>();
    public DbSet<SoundMessage> SoundMessages => Set<SoundMessage>();
    public DbSet<CameraConfig> Cameras => Set<CameraConfig>();
    public DbSet<PrintConfig> PrintConfigs => Set<PrintConfig>();
    public DbSet<SmsConfig> SmsConfigs => Set<SmsConfig>();
    public DbSet<SmsTemplate> SmsTemplates => Set<SmsTemplate>();
    public DbSet<SmsLog> SmsLogs => Set<SmsLog>();
    public DbSet<BackupConfig> BackupConfigs => Set<BackupConfig>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(50);
            e.Property(x => x.FullName).HasMaxLength(100);
            e.Property(x => x.FullNameHi).HasMaxLength(100);
            e.Property(x => x.Mobile).HasMaxLength(10);
            e.Property(x => x.Email).HasMaxLength(100);
        });
        b.Entity<Role>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Permission>().HasIndex(x => x.Code).IsUnique();
        b.Entity<UserRole>().HasKey(x => new { x.UserId, x.RoleId });
        b.Entity<RolePermission>().HasKey(x => new { x.RoleId, x.PermissionId });
        b.Entity<RefreshToken>().HasIndex(x => x.TokenHash);
        b.Entity<RefreshToken>().HasIndex(x => x.UserId);
        b.Entity<AuditLog>().HasIndex(x => x.Timestamp);
        b.Entity<AuditLog>().HasIndex(x => new { x.Module, x.Action });

        b.Entity<Zone>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever(); // business serial via NumberSequence
            e.HasIndex(x => x.ZoneName).IsUnique();
            e.Property(x => x.ZoneName).HasMaxLength(100);
            e.Property(x => x.ZoneNameHi).HasMaxLength(100);
            e.Property(x => x.ZoneCode).HasMaxLength(20);
        });
        b.Entity<Village>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever(); // starts at 101 via NumberSequence
            e.HasIndex(x => new { x.ZoneId, x.VillageName }).IsUnique();
            e.Property(x => x.VillageName).HasMaxLength(100);
            e.Property(x => x.VillageNameHi).HasMaxLength(100);
            e.Property(x => x.Mobile).HasMaxLength(10);
        });
        b.Entity<Bank>(e =>
        {
            e.HasIndex(x => new { x.BankName, x.BranchName }).IsUnique();
            e.HasIndex(x => x.IFSC);
            e.Property(x => x.IFSC).HasMaxLength(11);
        });
        b.Entity<Grower>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.VillageId, x.GrowerSequence }).IsUnique();
            e.HasIndex(x => x.GrowerCode).IsUnique();
            e.HasIndex(x => x.AadhaarHash).IsUnique().HasFilter(null);
            e.HasIndex(x => x.Mobile);
            e.Property(x => x.GrowerCode).HasMaxLength(20);
            e.Property(x => x.GrowerNameHi).HasMaxLength(100);
            e.Property(x => x.FatherNameHi).HasMaxLength(100);
            e.Property(x => x.Mobile).HasMaxLength(10);
            e.HasOne(x => x.Bank).WithMany().HasForeignKey(x => x.BankId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<VehicleType>(e => { e.HasIndex(x => x.VehicleTypeName).IsUnique(); e.Property(x => x.VehicleTypeNameHi).HasMaxLength(50); });
        b.Entity<VarietyType>().HasIndex(x => x.VarietyTypeName).IsUnique();
        b.Entity<Variety>().HasIndex(x => new { x.VarietyTypeId, x.VarietyName }).IsUnique();
        b.Entity<RateMaster>(e =>
        {
            e.Property(x => x.Rate).HasPrecision(12, 2);
            e.HasIndex(x => new { x.VarietyTypeId, x.EffectiveFrom });
        });
        b.Entity<Item>(e => { e.HasIndex(x => x.ItemName).IsUnique(); e.Property(x => x.ItemNameHi).HasMaxLength(50); });
        b.Entity<Party>(e =>
        {
            e.HasIndex(x => x.PartyName);
            e.HasIndex(x => x.Mobile);
            e.Property(x => x.Mobile).HasMaxLength(10);
            e.Property(x => x.PartyNameHi).HasMaxLength(100);
        });
        b.Entity<PaymentModeMaster>().HasIndex(x => x.ModeCode).IsUnique();

        b.Entity<ExpenseType>(e =>
        {
            e.HasIndex(x => x.ExpenseName).IsUnique();
            e.Property(x => x.ExpenseName).HasMaxLength(100).IsRequired();
            e.Property(x => x.ExpenseNameHi).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(250);
        });
        b.Entity<Expense>(e =>
        {
            e.HasIndex(x => x.ExpenseDate);
            e.HasIndex(x => x.ExpenseTypeId);
            e.Property(x => x.Quantity).HasPrecision(14, 3);
            e.Property(x => x.UnitCharge).HasPrecision(14, 2);
            e.Property(x => x.TotalAmount).HasPrecision(14, 2);
            e.Property(x => x.Remarks).HasMaxLength(500);
            e.HasOne(x => x.ExpenseType).WithMany().HasForeignKey(x => x.ExpenseTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Purchase>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever(); // continuous business serial
            e.HasIndex(x => x.GrowerCode);
            e.HasIndex(x => x.VillageId);
            e.HasIndex(x => x.AdviceNumber);
            e.HasIndex(x => x.PaymentStatus);
            e.HasIndex(x => x.GrossTareStatus);
            e.HasIndex(x => x.GrossDateTime);
            e.HasIndex(x => x.LockStatus);
            foreach (var p in new[] { "ScaleReadingGrossKg", "GrossWeightQuintal", "ScaleReadingTareKg", "TareWeightQuintal",
                "NetWeightQuintal", "CuttingPercent", "CuttingWeightQuintal", "TaxPercent", "TaxWeightQuintal",
                "FinalWeightQuintal", "Rate" })
                e.Property(p).HasPrecision(12, 2);
            e.Property(x => x.PurchaseAmount).HasPrecision(14, 2);
            e.HasOne(x => x.Grower).WithMany().HasForeignKey(x => x.GrowerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.VehicleType).WithMany().HasForeignKey(x => x.VehicleTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Variety).WithMany().HasForeignKey(x => x.VarietyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Season).WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PurchaseImage>().HasIndex(x => x.PurchaseId);

        b.Entity<SalePurchase>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            // SalePurchase validation already limits a vehicle number to 4-15 characters;
            // persist that same domain limit because it participates in a composite index.
            e.Property(x => x.VehicleNumber).HasMaxLength(15).IsRequired();
            // Status is queried for the pending-grid/report workflow and is indexed below.
            // A bounded value maps to nvarchar(32), which is index-compatible on SQL Server.
            e.Property(x => x.WeighmentStatus).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.WeighmentStatus);
            e.HasIndex(x => x.TareDateTime);
            e.HasIndex(x => x.GrossDateTime);
            e.HasIndex(x => new { x.PartyId, x.VehicleNumber });
            foreach (var p in new[] { "ScaleReadingTareKg", "TareWeightQuintal", "ScaleReadingGrossKg", "GrossWeightQuintal", "FinalWeightQuintal", "Rate" })
                e.Property(p).HasPrecision(12, 2);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.VehicleType).WithMany().HasForeignKey(x => x.VehicleTypeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SalePurchaseImage>().HasIndex(x => x.SalePurchaseId);

        b.Entity<LoanTypeMaster>().HasIndex(x => x.LoanTypeName).IsUnique();
        b.Entity<Loan>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever(); // continuous business serial (LoanId)
            e.HasIndex(x => x.GrowerCode);
            e.HasIndex(x => x.LoanStatus);
            e.Property(x => x.LoanAmount).HasPrecision(14, 2);
            e.Property(x => x.RecoveredAmount).HasPrecision(14, 2);
            e.Property(x => x.OutstandingAmount).HasPrecision(14, 2);
            e.HasOne(x => x.Grower).WithMany().HasForeignKey(x => x.GrowerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.LoanType).WithMany().HasForeignKey(x => x.LoanTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Season).WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LoanRecovery>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever(); // continuous business serial (LRId)
            e.HasIndex(x => x.LoanId);
            e.HasIndex(x => x.GrowerCode);
            e.Property(x => x.RecoveryAmount).HasPrecision(14, 2);
            e.HasOne(x => x.Loan).WithMany().HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Payment>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever(); // continuous business serial (PaymentId)
            e.HasIndex(x => x.GrowerCode);
            e.HasIndex(x => x.AdviceNumber);
            e.HasIndex(x => x.PaymentStatus);
            e.Property(x => x.TotalPurchaseAmount).HasPrecision(14, 2);
            e.Property(x => x.LoanDeductedAmount).HasPrecision(14, 2);
            e.Property(x => x.NetPayableAmount).HasPrecision(14, 2);
            e.HasOne(x => x.Grower).WithMany().HasForeignKey(x => x.GrowerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PaymentMode).WithMany().HasForeignKey(x => x.PaymentModeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Season).WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PaymentPurchase>(e =>
        {
            e.HasIndex(x => x.PaymentId);
            e.HasIndex(x => x.PurchaseId);
            e.Property(x => x.PurchaseAmountAtPayment).HasPrecision(14, 2);
            e.HasOne(x => x.Payment).WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Purchase).WithMany().HasForeignKey(x => x.PurchaseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PaymentImage>().HasIndex(x => x.PaymentId);

        b.Entity<CashBookEntry>(e =>
        {
            e.HasIndex(x => x.EntryDate);
            e.HasIndex(x => x.PaymentId);
            e.HasIndex(x => x.EntryType);
            e.Property(x => x.EntryType).HasMaxLength(24).IsRequired();
            e.Property(x => x.SourceType).HasMaxLength(32).IsRequired();
            e.Property(x => x.SourceName).HasMaxLength(150);
            e.Property(x => x.GrowerCode).HasMaxLength(20);
            e.Property(x => x.GrowerName).HasMaxLength(100);
            e.Property(x => x.ReferenceNumber).HasMaxLength(100);
            e.Property(x => x.Remarks).HasMaxLength(500);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.NetPayableAmount).HasPrecision(14, 2);
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Grower>().WithMany().HasForeignKey(x => x.GrowerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SmsTemplate>().HasIndex(x => new { x.EventCode, x.Language }).IsUnique();
        b.Entity<SmsLog>(e =>
        {
            e.HasIndex(x => new { x.EventCode, x.ReferenceId }).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.GrowerId);
            e.HasIndex(x => x.PartyId);
        });

        b.Entity<WeightRuleConfig>(e =>
        {
            e.Property(x => x.MinimumWeightQuintal).HasPrecision(12, 2);
            e.Property(x => x.DefaultCuttingPercent).HasPrecision(5, 2);
            e.Property(x => x.DefaultTaxPercent).HasPrecision(5, 2);
        });
        b.Entity<SoundConfig>().Property(x => x.SpeechRate).HasPrecision(4, 2);
        b.Entity<SystemSetting>().HasIndex(x => x.Key).IsUnique();
        b.Entity<NumberSequence>().HasKey(x => x.Name);
        b.Entity<NumberSequence>().Property(x => x.Name).HasMaxLength(64);
        b.Entity<WeighingDevice>()
            .HasOne(x => x.ActiveStringProfile).WithMany().HasForeignKey(x => x.ActiveStringProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
