using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>Stores the GST selected at sale gross time and the immutable totals.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261014100000_AddSalePurchaseGst")]
public partial class AddSalePurchaseGst : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "GstPercent", table: "SalePurchases", type: "decimal(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>(name: "GstAmount", table: "SalePurchases", type: "decimal(14,2)", precision: 14, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "FinalAmount", table: "SalePurchases", type: "decimal(14,2)", precision: 14, scale: 2, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GstPercent", table: "SalePurchases");
        migrationBuilder.DropColumn(name: "GstAmount", table: "SalePurchases");
        migrationBuilder.DropColumn(name: "FinalAmount", table: "SalePurchases");
    }
}
