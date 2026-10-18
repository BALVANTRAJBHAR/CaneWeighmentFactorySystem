using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>
/// Separates the immutable Rate Master value captured at Gross from the editable
/// transaction rate stored on Purchase.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261018113000_AddPurchaseMasterRateSnapshot")]
public partial class AddPurchaseMasterRateSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "MasterRateAtGross",
            table: "Purchases",
            type: "decimal(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        // Recover the original master rate for existing rows from the rate period
        // that covered their Gross date. Fall back to Purchase.Rate only when no
        // historical master period exists.
        migrationBuilder.Sql(@"
UPDATE p
SET p.MasterRateAtGross = COALESCE(masterAtGross.Rate, p.Rate)
FROM Purchases p
OUTER APPLY
(
    SELECT TOP (1) r.Rate
    FROM Rates r
    WHERE r.VarietyTypeId = p.VarietyTypeId
      AND r.IsDeleted = 0
      AND r.Status = 1
      AND CONVERT(date, r.EffectiveFrom) <= CONVERT(date, p.GrossDateTime)
      AND (r.EffectiveTo IS NULL OR CONVERT(date, r.EffectiveTo) >= CONVERT(date, p.GrossDateTime))
    ORDER BY r.EffectiveFrom DESC, r.Id DESC
) masterAtGross;");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "MasterRateAtGross", table: "Purchases");
}
