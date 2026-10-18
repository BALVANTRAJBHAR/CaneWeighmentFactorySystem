using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>Adds developer-configurable logo dimensions for PDF and Excel report exports.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261018130000_AddReportLogoDimensions")]
public partial class AddReportLogoDimensions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ReportLogoWidth",
            table: "CompanyConfigs",
            type: "int",
            nullable: false,
            defaultValue: 180);

        migrationBuilder.AddColumn<int>(
            name: "ReportLogoHeight",
            table: "CompanyConfigs",
            type: "int",
            nullable: false,
            defaultValue: 70);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ReportLogoWidth", table: "CompanyConfigs");
        migrationBuilder.DropColumn(name: "ReportLogoHeight", table: "CompanyConfigs");
    }
}
