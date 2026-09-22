using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>Preserves the recipient-bank details used for each completed BANK payment.
/// Historical rows remain nullable and use the grower master as a display fallback.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260919110000_AddPaymentBankSnapshots")]
public partial class AddPaymentBankSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "AccountHolderNameAtPayment", table: "Payments", type: "nvarchar(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>(name: "BankNameAtPayment", table: "Payments", type: "nvarchar(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>(name: "BankBranchAtPayment", table: "Payments", type: "nvarchar(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>(name: "BankIfscAtPayment", table: "Payments", type: "nvarchar(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>(name: "BankAccountNumberAtPayment", table: "Payments", type: "nvarchar(64)", maxLength: 64, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AccountHolderNameAtPayment", table: "Payments");
        migrationBuilder.DropColumn(name: "BankNameAtPayment", table: "Payments");
        migrationBuilder.DropColumn(name: "BankBranchAtPayment", table: "Payments");
        migrationBuilder.DropColumn(name: "BankIfscAtPayment", table: "Payments");
        migrationBuilder.DropColumn(name: "BankAccountNumberAtPayment", table: "Payments");
    }
}
