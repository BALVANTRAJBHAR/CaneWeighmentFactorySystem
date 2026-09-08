using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260906120000_AddPrintProfiles")]
public partial class AddPrintProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "DotMatrixPrinterName", table: "PrintConfigs", type: "nvarchar(max)", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "A4PrinterName", table: "PrintConfigs", type: "nvarchar(max)", nullable: false, defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DotMatrixPrinterName", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "A4PrinterName", table: "PrintConfigs");
    }
}
