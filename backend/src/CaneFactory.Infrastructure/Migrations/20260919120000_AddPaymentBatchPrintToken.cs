using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddPaymentBatchPrintToken : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BatchPrintToken",
            table: "Payments",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Payments_BatchPrintToken",
            table: "Payments",
            column: "BatchPrintToken");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Payments_BatchPrintToken",
            table: "Payments");

        migrationBuilder.DropColumn(
            name: "BatchPrintToken",
            table: "Payments");
    }
}
