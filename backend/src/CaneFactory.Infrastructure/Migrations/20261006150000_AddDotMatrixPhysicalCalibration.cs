using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261006150000_AddDotMatrixPhysicalCalibration")]
public partial class AddDotMatrixPhysicalCalibration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "DotMatrixContentStartOffsetLines",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "DotMatrixHalfPageLines",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 54);

        migrationBuilder.AddColumn<int>(
            name: "DotMatrixLineSpacingUnits",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 20);

        migrationBuilder.AddColumn<int>(
            name: "DotMatrixNextFormTofLines",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 108);

        migrationBuilder.AddColumn<int>(
            name: "DotMatrixPostSlipFeedLines",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "DotMatrixTearLinePosition",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 54);

        migrationBuilder.Sql("""
            UPDATE PrintConfigs
            SET DotMatrixHalfPageLines = DotMatrixPageLines / 2,
                DotMatrixTearLinePosition = DotMatrixPageLines / 2,
                DotMatrixNextFormTofLines = DotMatrixPageLines;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DotMatrixContentStartOffsetLines", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "DotMatrixHalfPageLines", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "DotMatrixLineSpacingUnits", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "DotMatrixNextFormTofLines", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "DotMatrixPostSlipFeedLines", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "DotMatrixTearLinePosition", table: "PrintConfigs");
    }
}
