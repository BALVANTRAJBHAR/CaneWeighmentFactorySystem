using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>Adds a portable company logo plus independent payment/loan auto-print gates.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261022101500_AddCloudLogoAndIndependentAutoPrint")]
public partial class AddCloudLogoAndIndependentAutoPrint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "LogoData",
            table: "CompanyConfigs",
            type: "varbinary(max)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LogoContentType",
            table: "CompanyConfigs",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "PaymentAutoPrint",
            table: "PrintConfigs",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "LoanAutoPrint",
            table: "PrintConfigs",
            type: "bit",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "LogoData", table: "CompanyConfigs");
        migrationBuilder.DropColumn(name: "LogoContentType", table: "CompanyConfigs");
        migrationBuilder.DropColumn(name: "PaymentAutoPrint", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "LoanAutoPrint", table: "PrintConfigs");
    }
}
