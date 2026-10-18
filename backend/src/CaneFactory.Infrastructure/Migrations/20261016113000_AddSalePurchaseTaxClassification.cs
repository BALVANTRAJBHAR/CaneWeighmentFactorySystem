using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>Captures the supply classification and immutable GST split used by the sale tax report.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261016113000_AddSalePurchaseTaxClassification")]
public partial class AddSalePurchaseTaxClassification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "SalesPercent", table: "SalePurchases", type: "decimal(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>(name: "SalesAmount", table: "SalePurchases", type: "decimal(14,2)", precision: 14, scale: 2, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TaxSupplyType", table: "SalePurchases", type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "INTRA_STATE");
        migrationBuilder.AddColumn<decimal>(name: "SgstAmount", table: "SalePurchases", type: "decimal(14,2)", precision: 14, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "CgstAmount", table: "SalePurchases", type: "decimal(14,2)", precision: 14, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "IgstAmount", table: "SalePurchases", type: "decimal(14,2)", precision: 14, scale: 2, nullable: true);

        // Pre-existing transactions did not store the place-of-supply decision.  Default them to
        // intra-state and preserve their already-saved GST total exactly across the two components.
        migrationBuilder.Sql(@"
UPDATE SalePurchases
SET SalesAmount = COALESCE(SalesAmount, ROUND(COALESCE(Amount, 0) * SalesPercent / 100.0, 2)),
    CgstAmount = COALESCE(CgstAmount, ROUND(COALESCE(GstAmount, 0) / 2.0, 2)),
    SgstAmount = COALESCE(SgstAmount, COALESCE(GstAmount, 0) - ROUND(COALESCE(GstAmount, 0) / 2.0, 2)),
    IgstAmount = COALESCE(IgstAmount, 0)
WHERE TaxSupplyType = N'INTRA_STATE';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var name in new[] { "SalesPercent", "SalesAmount", "TaxSupplyType", "SgstAmount", "CgstAmount", "IgstAmount" })
            migrationBuilder.DropColumn(name: name, table: "SalePurchases");
    }
}
