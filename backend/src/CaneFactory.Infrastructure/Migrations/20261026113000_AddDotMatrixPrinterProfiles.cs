using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261026113000_AddDotMatrixPrinterProfiles")]
public partial class AddDotMatrixPrinterProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DotMatrixPrinterProfilesJson",
            table: "PrintConfigs",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "{}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DotMatrixPrinterProfilesJson",
            table: "PrintConfigs");
    }
}
