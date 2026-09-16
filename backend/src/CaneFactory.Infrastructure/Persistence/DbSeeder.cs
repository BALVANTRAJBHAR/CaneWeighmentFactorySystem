using CaneFactory.Application.Common;
using CaneFactory.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CaneFactory.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        await SeedPermissionsAsync(db);
        await SeedRolesAsync(db);
        await SeedDeveloperAsync(db, config);
        await SeedMastersAsync(db);
        await SeedWeighingAsync(db);
        await SeedSettingsAsync(db);
        await db.SaveChangesAsync();
    }

    private static readonly string[] AllActions =
    {
        Actions.View, Actions.Create, Actions.Edit, Actions.Delete, Actions.Approve, Actions.Lock, Actions.Unlock,
        Actions.Cancel, Actions.Reverse, Actions.Pay, Actions.Export, Actions.Print, Actions.Configure,
        Actions.ViewCamera, Actions.ViewSensitiveData
    };

    private static async Task SeedPermissionsAsync(AppDbContext db)
    {
        var existing = await db.Permissions.Select(p => p.Code).ToListAsync();
        foreach (var module in Modules.All)
            foreach (var action in AllActions)
            {
                var code = $"{module}.{action}";
                if (!existing.Contains(code))
                    db.Permissions.Add(new Permission { Code = code, Module = module, Action = action });
            }
        await db.SaveChangesAsync();
    }

    private static Dictionary<string, List<string>> RoleMatrix()
    {
        string[] masterView = Modules.Masters.Select(m => $"{m}.View").ToArray();
        List<string> Codes(string[] modules, params string[] actions) =>
            modules.SelectMany(m => actions.Select(a => $"{m}.{a}")).ToList();

        var admin = Codes(Modules.Masters, "View", "Create", "Edit", "Delete", "Export", "Print");
        admin.AddRange(Codes(new[] { "User" }, "View", "Create", "Edit", "Delete"));
        admin.AddRange(Codes(new[] { "Role" }, "View"));
        admin.AddRange(Codes(new[] { "Purchase" }, "View", "Create", "Edit", "Lock", "Unlock", "Cancel", "Export", "Print"));
        admin.AddRange(Codes(new[] { "Weighment" }, "View", "Create", "Edit", "Print"));
        admin.AddRange(Codes(new[] { "SalePurchase" }, "View", "Create", "Edit", "Cancel", "Export", "Print"));
        admin.AddRange(Codes(new[] { "Loan", "LoanRecovery", "Payment" }, "View", "Export", "Print"));
        admin.AddRange(Codes(new[] { "Expense" }, "View", "Create", "Edit", "Delete", "Export", "Print"));
        admin.AddRange(Codes(new[] { "CashBook" }, "View", "Create", "Export", "Print"));
        admin.AddRange(Codes(new[] { "Report" }, "View", "Export", "Print"));
        admin.AddRange(Codes(new[] { "Camera" }, "ViewCamera"));
        admin.AddRange(Codes(new[] { "Image", "CashEvidence" }, "View"));
        admin.AddRange(Codes(new[] { "Audit" }, "View"));
        admin.AddRange(Codes(new[] { "Dashboard", "Health", "UserGuide" }, "View"));
        admin.AddRange(Codes(new[] { "Company", "Season" }, "Configure"));
        admin.AddRange(Codes(new[] { "Backup" }, "View", "Configure"));

        var subAdmin = Codes(Modules.Masters, "View", "Create", "Edit");
        subAdmin.AddRange(Codes(new[] { "Purchase", "Weighment", "Report" }, "View"));
        subAdmin.AddRange(Codes(new[] { "Expense" }, "View", "Create", "Edit", "Delete"));
        subAdmin.AddRange(new[] { "Report.Export", "Report.Print", "Dashboard.View", "Health.View", "UserGuide.View", "Camera.ViewCamera", "Backup.View" });

        var accountant = Codes(new[] { "Loan", "LoanRecovery" }, "View", "Create", "Cancel", "Reverse", "Export", "Print");
        accountant.AddRange(Codes(new[] { "Payment" }, "View", "Create", "Pay", "Cancel", "Reverse", "Export", "Print"));
        accountant.AddRange(Codes(new[] { "CashEvidence" }, "View", "Create"));
        accountant.AddRange(Codes(new[] { "Report" }, "View", "Export", "Print"));
        accountant.AddRange(Codes(new[] { "Expense" }, "View", "Create", "Edit", "Delete", "Export", "Print"));
        accountant.AddRange(Codes(new[] { "CashBook" }, "View", "Create", "Export", "Print"));
        accountant.AddRange(new[] { "Purchase.View", "Grower.View", "Village.View", "Bank.View", "LoanType.View",
            "Dashboard.View", "Health.View", "UserGuide.View" });

        var op = new List<string>
        {
            "Weighment.View", "Weighment.Create", "Weighment.Edit", "Weighment.Print",
            "Purchase.View", "Purchase.Create", "Purchase.Edit",
            "Expense.View", "Expense.Create", "Expense.Edit",
            "CashBook.View", "CashBook.Create",
            "Grower.View", "Village.View", "Zone.View", "Vehicle.View", "Vehicle.Create",
            "VarietyType.View", "Variety.View", "Rate.View",
            "Report.View", "Report.Print", "Camera.ViewCamera", "Image.Create",
            "Dashboard.View", "Health.View", "UserGuide.View", "Print.Print"
        };

        var farmer = new List<string>
        {
            "Purchase.View", "Payment.View", "Loan.View", "LoanRecovery.View",
            "Report.View", "Dashboard.View", "UserGuide.View"
        };

        var salePurchase = new List<string>
        {
            "SalePurchase.View", "SalePurchase.Create", "SalePurchase.Edit", "SalePurchase.Print", "SalePurchase.Export",
            "Party.View", "Party.Create", "Item.View", "Vehicle.View", "Vehicle.Create",
            "Camera.ViewCamera", "Image.Create", "Report.View", "Report.Print", "Report.Export",
            "Dashboard.View", "Health.View", "UserGuide.View", "Print.Print"
        };

        return new Dictionary<string, List<string>>
        {
            ["Developer"] = new() { "*" },
            ["Admin"] = admin,
            ["SubAdmin"] = subAdmin,
            ["Accountant"] = accountant,
            ["Operator"] = op,
            ["Farmer"] = farmer,
            ["SalePurchase"] = salePurchase
        };
    }

    private static async Task SeedRolesAsync(AppDbContext db)
    {
        var allPerms = await db.Permissions.ToListAsync();
        foreach (var (roleName, codes) in RoleMatrix())
        {
            var role = await db.Roles.Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Name == roleName);
            if (role == null)
            {
                role = new Role { Name = roleName, IsSystem = true, Description = $"{roleName} system role" };
                db.Roles.Add(role);
                await db.SaveChangesAsync();
            }
            var target = codes.Contains("*")
                ? allPerms
                : allPerms.Where(p => codes.Contains(p.Code)).ToList();
            var have = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
            foreach (var p in target.Where(p => !have.Contains(p.Id)))
                db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = p.Id });
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedDeveloperAsync(AppDbContext db, IConfiguration config)
    {
        if (await db.Users.AnyAsync(u => u.Username == "developer")) return;
        var tempPassword = config["Seed:DeveloperPassword"] ?? "ChangeMe@2026";
        var user = new User
        {
            Username = "developer",
            FullName = "System Developer",
            Mobile = "9999999999",
            Email = "developer@factory.local",
            MustChangePassword = true // temporary password: forced change on first login
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, tempPassword);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var devRole = await db.Roles.FirstAsync(r => r.Name == "Developer");
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = devRole.Id });
        await db.SaveChangesAsync();
    }

    private static async Task SeedMastersAsync(AppDbContext db)
    {
        if (!await db.VehicleTypes.AnyAsync())
            db.VehicleTypes.AddRange(new[] { "Truck", "Tractor", "Cart" }
                .Select(n => new VehicleType { VehicleTypeName = n }));

        if (!await db.VarietyTypes.AnyAsync())
        {
            var early = new VarietyType { VarietyTypeName = "Early" };
            var general = new VarietyType { VarietyTypeName = "General" };
            var midLate = new VarietyType { VarietyTypeName = "Mid Late" };
            db.VarietyTypes.AddRange(early, general, midLate);
            await db.SaveChangesAsync();
            db.Varieties.AddRange(new[] { "CO 18", "CO 32", "SO 30", "SO 40" }
                .Select(n => new Variety { VarietyTypeId = early.Id, VarietyName = n }));
        }

        if (!await db.Items.AnyAsync())
            db.Items.AddRange(new[] { "Sugar", "Gud", "Bagasse", "Molasses" }
                .Select(n => new Item { ItemName = n }));

        if (!await db.LoanTypes.AnyAsync())
            db.LoanTypes.AddRange(new[] { "Fertilizer Loan", "Seed Loan", "Equipment Loan", "Emergency Loan" }
                .Select(n => new LoanTypeMaster { LoanTypeName = n }));

        if (!await db.PaymentModes.AnyAsync())
            db.PaymentModes.AddRange(
                new PaymentModeMaster { ModeCode = "CASH", ModeName = "Cash" },
                new PaymentModeMaster { ModeCode = "BANK", ModeName = "Bank" },
                new PaymentModeMaster { ModeCode = "MOBILE_UPI", ModeName = "Mobile UPI" });

        if (!await db.Seasons.AnyAsync())
            db.Seasons.Add(new Season { SeasonName = "2026-27", StartDate = new DateTime(2026, 10, 1), IsActive = true });

        if (!await db.CompanyConfigs.AnyAsync())
            db.CompanyConfigs.Add(new CompanyConfig
            {
                CompanyName = "Shri Ganesh Khandsari Udyog",
                Address = "Factory Road, District Headquarters, Uttar Pradesh",
                DefaultLanguage = "en",
                ThemeColor = "#1B5E20"
            });
        await db.SaveChangesAsync();
    }

    private static async Task SeedWeighingAsync(AppDbContext db)
    {
        // Older installations may contain the Type 15 profile but no device
        // row (the previous seed tied both records to one condition).  Keep
        // the profile and the physical-device bootstrap independent so a
        // freshly published server always exposes a configurable device.
        var type15 = await db.StringProfiles
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.StringType == "15");
        if (type15 == null)
        {
            type15 = new StringProfile
            {
                StringProfileName = "String Type 15 (Supplied Indicator)",
                StringType = "15",
                ParserType = "FixedPosition",
                StartByte = "0x02",
                SignByte = "0x20/0x2D",
                SignPosition = 2,
                WeightStartPosition = 3,
                WeightLength = 6,
                WeightCharacterOrder = "Normal",
                DecimalPlaces = 0,
                WeightUnit = "KG",
                EndByte = "0x03",
                CarriageReturn = true,
                LineFeed = true,
                PositiveSignValue = "0x20",
                NegativeSignValue = "0x2D",
                StableWeightRule = "SameValueDuration",
                StableWeightDurationMs = 1500,
                FrameValidation = true,
                InvalidFrameHandling = "Ignore"
            };
            db.StringProfiles.Add(type15);
            await db.SaveChangesAsync();
        }

        if (!await db.WeighingDevices.AnyAsync(d => !d.IsDeleted))
        {
            db.WeighingDevices.Add(new WeighingDevice
            {
                DeviceName = "Main Weighbridge Indicator",
                Manufacturer = "As Supplied",
                ModelNumber = "String Type 15",
                ConnectionType = "RS232",
                ComPort = "COM1",
                BaudRate = 2400,
                Parity = "None",
                DataBits = 8,
                StopBits = 1,
                FlowControl = "None",
                ActiveConfiguration = true,
                ActiveStringProfileId = type15.Id
            });
        }

        if (!await db.WeightRules.AnyAsync())
            db.WeightRules.Add(new WeightRuleConfig());

        if (!await db.SoundConfigs.AnyAsync())
        {
            db.SoundConfigs.Add(new SoundConfig());
            db.SoundMessages.AddRange(
                new SoundMessage { EventCode = "BELOW_MINIMUM", LanguageCode = "hi", MessageText = "कृपया गाड़ी को प्लेटफॉर्म पर खड़ा करें।" },
                new SoundMessage { EventCode = "BELOW_MINIMUM", LanguageCode = "en", MessageText = "Please position the vehicle on the platform." },
                new SoundMessage { EventCode = "WEIGHING_ACTIVE", LanguageCode = "hi", MessageText = "आपकी गाड़ी का तौल हो रहा है, कृपया प्रतीक्षा करें।" },
                new SoundMessage { EventCode = "WEIGHING_ACTIVE", LanguageCode = "en", MessageText = "Your vehicle is being weighed, please wait." },
                new SoundMessage { EventCode = "WEIGHMENT_COMPLETED", LanguageCode = "hi", MessageText = "तौल हो चुका है, कृपया गाड़ी को प्लेटफॉर्म से नीचे उतारें।" },
                new SoundMessage { EventCode = "WEIGHMENT_COMPLETED", LanguageCode = "en", MessageText = "Weighment completed, please move the vehicle off the platform." });
        }

        if (!await db.SoundMessages.AnyAsync(m => m.EventCode == "IMAGE_CAPTURED" && m.LanguageCode == "hi"))
            db.SoundMessages.Add(new SoundMessage { EventCode = "IMAGE_CAPTURED", LanguageCode = "hi", MessageText = "तौल की तस्वीरें सुरक्षित हो गई हैं।" });
        if (!await db.SoundMessages.AnyAsync(m => m.EventCode == "IMAGE_CAPTURED" && m.LanguageCode == "en"))
            db.SoundMessages.Add(new SoundMessage { EventCode = "IMAGE_CAPTURED", LanguageCode = "en", MessageText = "Weighment images have been captured and saved." });

        if (!await db.PrintConfigs.AnyAsync())
            db.PrintConfigs.Add(new PrintConfig { PrinterName = "TVS MSP 270 Classic Plus" });

        if (!await db.SmsConfigs.AnyAsync())
        {
            db.SmsConfigs.Add(new SmsConfig { Enabled = false, Language = "hi", HttpMethod = "POST", RequestContentType = "application/json" });
            db.SmsTemplates.AddRange(
                new SmsTemplate { EventCode = "TARE_COMPLETED", Language = "hi",
                    MessageTemplate = "प्रिय {GrowerName}, आपकी गन्ना तौल पूर्ण हुई। वाहन: {VehicleNumber}, अंतिम वजन: {FinalWeight} क्विंटल, राशि: Rs {PurchaseAmount}. धन्यवाद।" },
                new SmsTemplate { EventCode = "TARE_COMPLETED", Language = "en",
                    MessageTemplate = "Dear {GrowerName}, your cane weighment is complete. Vehicle: {VehicleNumber}, Final Weight: {FinalWeight} Qtl, Amount: Rs {PurchaseAmount}. Thank you." },
                new SmsTemplate { EventCode = "PAYMENT_COMPLETED", Language = "hi",
                    MessageTemplate = "प्रिय {GrowerName}, आपका भुगतान पूर्ण हुआ। अग्रिम क्रमांक: {AdviceNumber}, कुल राशि: Rs {TotalPurchaseAmount}, ऋण कटौती: Rs {LoanDeducted}, शुद्ध देय: Rs {NetPayable} ({PaymentMode})." },
                new SmsTemplate { EventCode = "PAYMENT_COMPLETED", Language = "en",
                    MessageTemplate = "Dear {GrowerName}, your payment is complete. Advice No: {AdviceNumber}, Total: Rs {TotalPurchaseAmount}, Loan Deducted: Rs {LoanDeducted}, Net Payable: Rs {NetPayable} ({PaymentMode})." });
        }
        // Existing installations already have an SMS configuration; seed the new event template
        // independently so enabling SalePurchase SMS never requires recreating that configuration.
        if (!await db.SmsTemplates.AnyAsync(t => t.EventCode == "SALE_PURCHASE_COMPLETED" && t.Language == "hi"))
            db.SmsTemplates.Add(new SmsTemplate { EventCode = "SALE_PURCHASE_COMPLETED", Language = "hi",
                MessageTemplate = "बिक्री/खरीद क्रमांक {SalePurchaseId} पूर्ण हुआ। पार्टी: {PartyName}, अंतिम वजन: {FinalWeight} क्विंटल, राशि: Rs {Amount}." });
        if (!await db.SmsTemplates.AnyAsync(t => t.EventCode == "SALE_PURCHASE_COMPLETED" && t.Language == "en"))
            db.SmsTemplates.Add(new SmsTemplate { EventCode = "SALE_PURCHASE_COMPLETED", Language = "en",
                MessageTemplate = "SalePurchase {SalePurchaseId} completed. Party: {PartyName}, Final Weight: {FinalWeight} Qtl, Amount: Rs {Amount}." });

        if (!await db.BackupConfigs.AnyAsync())
            db.BackupConfigs.Add(new BackupConfig());

        await db.SaveChangesAsync();
    }

    private static async Task SeedSettingsAsync(AppDbContext db)
    {
        var defaults = new Dictionary<string, string>
        {
            ["CameraSystemEnabled"] = "true",
            ["ImageCaptureEnabled"] = "true",
            // Blank means the capture service selects the ready C:/D:/E: drive
            // with the most free space and creates WeighmentImage there.
            ["ImageStorageRoot"] = "",
            ["Health.Cpu.Warning"] = "70",
            ["Health.Cpu.Critical"] = "90",
            ["Health.Ram.Warning"] = "70",
            ["Health.Ram.Critical"] = "90",
            ["Health.Storage.Warning"] = "75",
            ["Health.Storage.Critical"] = "85",
            ["Security.MaxFailedLogins"] = "5",
            ["Security.LockoutMinutes"] = "15",
            ["Security.RequireOtpForAuthenticatedPasswordChange"] = "false",
            // Empty means no expiry date has been issued yet. Use yyyy-MM-dd when licensing is enabled.
            ["LicenseEndDate"] = ""
        };
        var existing = await db.SystemSettings.Select(s => s.Key).ToListAsync();
        foreach (var (k, v) in defaults)
            if (!existing.Contains(k))
                db.SystemSettings.Add(new SystemSetting { Key = k, Value = v });
        await db.SaveChangesAsync();
    }
}
