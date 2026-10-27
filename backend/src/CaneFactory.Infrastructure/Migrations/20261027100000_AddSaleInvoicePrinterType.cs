using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CaneFactory.Infrastructure.Persistence;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261027100000_AddSaleInvoicePrinterType")]
public partial class AddSaleInvoicePrinterType : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SalePurchaseInvoicePrinterType",
            table: "PrintConfigs",
            type: "nvarchar(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "A4");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SalePurchaseInvoicePrinterType",
            table: "PrintConfigs");
    }
}
