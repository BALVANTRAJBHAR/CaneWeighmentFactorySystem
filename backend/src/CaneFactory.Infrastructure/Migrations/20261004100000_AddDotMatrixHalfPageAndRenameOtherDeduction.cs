using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CaneFactory.Infrastructure.Persistence;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261004100000_AddDotMatrixHalfPageAndRenameOtherDeduction")]
public partial class AddDotMatrixHalfPageAndRenameOtherDeduction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "TaxPercent",
            table: "Purchases",
            newName: "OtherDeductionPercent");

        migrationBuilder.RenameColumn(
            name: "TaxWeightQuintal",
            table: "Purchases",
            newName: "OtherDeductionWeightQuintal");

        migrationBuilder.RenameColumn(
            name: "DefaultTaxPercent",
            table: "WeightRules",
            newName: "DefaultOtherDeductionPercent");

        migrationBuilder.AddColumn<bool>(
            name: "DotMatrixPrintHeader",
            table: "PrintConfigs",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<int>(
            name: "DotMatrixPageLines",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 108);

        migrationBuilder.AddColumn<int>(
            name: "DotMatrixHeaderReservedLines",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 9);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DotMatrixPrintHeader", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "DotMatrixPageLines", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "DotMatrixHeaderReservedLines", table: "PrintConfigs");

        migrationBuilder.RenameColumn(
            name: "OtherDeductionPercent",
            table: "Purchases",
            newName: "TaxPercent");

        migrationBuilder.RenameColumn(
            name: "OtherDeductionWeightQuintal",
            table: "Purchases",
            newName: "TaxWeightQuintal");

        migrationBuilder.RenameColumn(
            name: "DefaultOtherDeductionPercent",
            table: "WeightRules",
            newName: "DefaultTaxPercent");
    }
}
