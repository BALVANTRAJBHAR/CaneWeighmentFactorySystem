using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>Adds the operator-editable Hindi display name for cane variety types.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261017170000_AddVarietyTypeHindi")]
public partial class AddVarietyTypeHindi : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "VarietyTypeNameHi",
            table: "VarietyTypes",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Purchases_VarietyTypes_VarietyTypeId",
            table: "Purchases",
            column: "VarietyTypeId",
            principalTable: "VarietyTypes",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        // Preserve familiar Hindi labels for the factory's standard seeded types,
        // without overwriting an already-entered Hindi name.
        migrationBuilder.Sql(@"
UPDATE VarietyTypes
SET VarietyTypeNameHi = CASE VarietyTypeName
    WHEN N'Early' THEN N'अगेती'
    WHEN N'General' THEN N'सामान्य'
    WHEN N'Mid Late' THEN N'मध्यम देर'
    ELSE VarietyTypeNameHi
END
WHERE NULLIF(LTRIM(RTRIM(VarietyTypeNameHi)), N'') IS NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Purchases_VarietyTypes_VarietyTypeId", table: "Purchases");
        migrationBuilder.DropColumn(name: "VarietyTypeNameHi", table: "VarietyTypes");
    }
}
