using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>
/// Sale weighment is tare-first: the tare receipt needs one copy and the
/// completed gross receipt needs two.  These counts must not inherit the
/// cane-purchase Gross/Tare counts.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261011090000_AddSeparateSaleWeighmentPrintCounts")]
public partial class AddSeparateSaleWeighmentPrintCounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "SalePurchaseGrossCopies",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 2);

        migrationBuilder.AddColumn<int>(
            name: "SalePurchaseTareCopies",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 1);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SalePurchaseGrossCopies", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "SalePurchaseTareCopies", table: "PrintConfigs");
    }
}
