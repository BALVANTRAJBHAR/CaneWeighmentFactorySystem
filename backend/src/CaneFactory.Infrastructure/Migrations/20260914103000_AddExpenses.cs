using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260914103000_AddExpenses")]
public partial class AddExpenses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ExpenseTypes",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ExpenseName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ExpenseNameHi = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<bool>(type: "bit", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ExpenseTypes", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Expenses",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ExpenseTypeId = table.Column<int>(type: "int", nullable: false),
                ExpenseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: false),
                UnitCharge = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                TotalAmount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
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
                table.PrimaryKey("PK_Expenses", x => x.Id);
                table.ForeignKey("FK_Expenses_ExpenseTypes_ExpenseTypeId", x => x.ExpenseTypeId,
                    principalTable: "ExpenseTypes", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_ExpenseTypes_ExpenseName", table: "ExpenseTypes", column: "ExpenseName", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Expenses_ExpenseDate", table: "Expenses", column: "ExpenseDate");
        migrationBuilder.CreateIndex(name: "IX_Expenses_ExpenseTypeId", table: "Expenses", column: "ExpenseTypeId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Expenses");
        migrationBuilder.DropTable(name: "ExpenseTypes");
    }
}
