using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104110000_AddSaleDeductionsAndCorrectionAuthority")]
public partial class AddSaleDeductionsAndCorrectionAuthority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "ItemType", table: "SalePurchases", type: "nvarchar(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "CuttingPercent", table: "SalePurchases", type: "decimal(12,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>(name: "CuttingWeightQuintal", table: "SalePurchases", type: "decimal(12,2)", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "KantaChhootKgPerQuintal", table: "SalePurchases", type: "decimal(12,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>(name: "KantaChhootWeightQuintal", table: "SalePurchases", type: "decimal(12,2)", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "SalePurchaseAmountCalculationPrintEnabled", table: "PrintConfigs", type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>(name: "CanAuthorizeWeighmentCorrections", table: "Users", type: "bit", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ItemType", table: "SalePurchases");
        migrationBuilder.DropColumn(name: "CuttingPercent", table: "SalePurchases");
        migrationBuilder.DropColumn(name: "CuttingWeightQuintal", table: "SalePurchases");
        migrationBuilder.DropColumn(name: "KantaChhootKgPerQuintal", table: "SalePurchases");
        migrationBuilder.DropColumn(name: "KantaChhootWeightQuintal", table: "SalePurchases");
        migrationBuilder.DropColumn(name: "SalePurchaseAmountCalculationPrintEnabled", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "CanAuthorizeWeighmentCorrections", table: "Users");
    }
}
