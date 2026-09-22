using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(AppDbContext))]
[Migration("20260919133000_AddPaymentEvidencePurchaseLink")]
public partial class AddPaymentEvidencePurchaseLink : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "PurchaseId",
            table: "PaymentImages",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_PaymentImages_PaymentId_PurchaseId",
            table: "PaymentImages",
            columns: new[] { "PaymentId", "PurchaseId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PaymentImages_PaymentId_PurchaseId",
            table: "PaymentImages");

        migrationBuilder.DropColumn(
            name: "PurchaseId",
            table: "PaymentImages");
    }
}
