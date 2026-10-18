using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>Stores the Sale Weighment tax-invoice switch, company invoice details and item HSN/SAC.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261016103000_AddSaleTaxInvoiceConfiguration")]
public partial class AddSaleTaxInvoiceConfiguration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "SalePurchaseInvoiceEnabled", table: "PrintConfigs", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "HsnSacCode", table: "Items", type: "nvarchar(32)", maxLength: 32, nullable: true);

        foreach (var name in new[]
        {
            "InvoiceRoadStreet", "InvoiceNearbyLandmark", "InvoiceLocality", "InvoiceCity", "InvoiceDistrict",
            "InvoiceState", "InvoicePinCode", "InvoicePhone", "InvoiceEmail", "InvoiceGstin", "InvoiceTermsAndConditions"
        })
            migrationBuilder.AddColumn<string>(name: name, table: "CompanyConfigs", type: "nvarchar(max)", nullable: true);

        // Seed only empty values. Existing factory-entered company data is never overwritten.
        migrationBuilder.Sql(@"
UPDATE CompanyConfigs SET
    InvoiceRoadStreet = COALESCE(NULLIF(InvoiceRoadStreet, N''), N'Kodsa'),
    InvoiceNearbyLandmark = COALESCE(NULLIF(InvoiceNearbyLandmark, N''), N'Kodsa'),
    InvoiceLocality = COALESCE(NULLIF(InvoiceLocality, N''), N'Kodsa'),
    InvoiceCity = COALESCE(NULLIF(InvoiceCity, N''), N'Khairi'),
    InvoiceDistrict = COALESCE(NULLIF(InvoiceDistrict, N''), N'Narsinghpur'),
    InvoiceState = COALESCE(NULLIF(InvoiceState, N''), N'Madhya Pradesh'),
    InvoicePinCode = COALESCE(NULLIF(InvoicePinCode, N''), N'487221'),
    InvoicePhone = COALESCE(NULLIF(InvoicePhone, N''), N'7987376722, 8828855999'),
    InvoiceEmail = COALESCE(NULLIF(InvoiceEmail, N''), N'afofarm91@gmail.com'),
    InvoiceGstin = COALESCE(NULLIF(InvoiceGstin, N''), N'23ACLFA2268H1ZH'),
    InvoiceTermsAndConditions = COALESCE(NULLIF(InvoiceTermsAndConditions, N''), N'Goods once sold will not be taken back.')
WHERE IsDeleted = 0;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SalePurchaseInvoiceEnabled", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "HsnSacCode", table: "Items");
        foreach (var name in new[]
        {
            "InvoiceRoadStreet", "InvoiceNearbyLandmark", "InvoiceLocality", "InvoiceCity", "InvoiceDistrict",
            "InvoiceState", "InvoicePinCode", "InvoicePhone", "InvoiceEmail", "InvoiceGstin", "InvoiceTermsAndConditions"
        })
            migrationBuilder.DropColumn(name: name, table: "CompanyConfigs");
    }
}
