using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260914110000_AddCashBook")]
public partial class AddCashBook : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CashBookEntries",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                EntryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                EntryType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                SourceName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Amount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                PaymentId = table.Column<int>(type: "int", nullable: true),
                GrowerId = table.Column<int>(type: "int", nullable: true),
                GrowerCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                GrowerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                NetPayableAmount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<bool>(type: "bit", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CashBookEntries", x => x.Id);
                table.ForeignKey("FK_CashBookEntries_Growers_GrowerId", x => x.GrowerId,
                    principalTable: "Growers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CashBookEntries_Payments_PaymentId", x => x.PaymentId,
                    principalTable: "Payments", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "IX_CashBookEntries_EntryDate", table: "CashBookEntries", column: "EntryDate");
        migrationBuilder.CreateIndex(name: "IX_CashBookEntries_EntryType", table: "CashBookEntries", column: "EntryType");
        migrationBuilder.CreateIndex(name: "IX_CashBookEntries_GrowerId", table: "CashBookEntries", column: "GrowerId");
        migrationBuilder.CreateIndex(name: "IX_CashBookEntries_PaymentId", table: "CashBookEntries", column: "PaymentId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "CashBookEntries");
}
