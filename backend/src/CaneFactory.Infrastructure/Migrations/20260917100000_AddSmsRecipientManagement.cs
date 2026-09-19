using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>Adds event switches and named operational SMS recipients without deleting the legacy
/// SalePurchaseRecipients column, so existing production configuration remains safe to upgrade.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260917100000_AddSmsRecipientManagement")]
public partial class AddSmsRecipientManagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "CanePurchaseSmsEnabled", table: "SmsConfigs", type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>(name: "CanePaymentSmsEnabled", table: "SmsConfigs", type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>(name: "SalePurchaseSmsEnabled", table: "SmsConfigs", type: "bit", nullable: false, defaultValue: true);

        migrationBuilder.CreateTable(
            name: "SmsRecipients",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                RecipientName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                MobileNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                ReceiveCanePurchase = table.Column<bool>(type: "bit", nullable: false),
                ReceivePayment = table.Column<bool>(type: "bit", nullable: false),
                ReceiveSalePurchase = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<bool>(type: "bit", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_SmsRecipients", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_SmsRecipients_MobileNumber", table: "SmsRecipients", column: "MobileNumber", unique: true, filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SmsRecipients");
        migrationBuilder.DropColumn(name: "CanePurchaseSmsEnabled", table: "SmsConfigs");
        migrationBuilder.DropColumn(name: "CanePaymentSmsEnabled", table: "SmsConfigs");
        migrationBuilder.DropColumn(name: "SalePurchaseSmsEnabled", table: "SmsConfigs");
    }
}
