using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using CaneFactory.Infrastructure.Persistence;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260905111500_AddSalePurchaseSmsRecipient")]
public partial class AddSalePurchaseSmsRecipient : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<int>(name: "GrowerId", table: "SmsLogs", type: "int", nullable: true, oldClrType: typeof(int), oldType: "int");
        migrationBuilder.AddColumn<int>(name: "PartyId", table: "SmsLogs", type: "int", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_SmsLogs_PartyId", table: "SmsLogs", column: "PartyId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_SmsLogs_PartyId", table: "SmsLogs");
        migrationBuilder.DropColumn(name: "PartyId", table: "SmsLogs");
        migrationBuilder.AlterColumn<int>(name: "GrowerId", table: "SmsLogs", type: "int", nullable: false, defaultValue: 0, oldClrType: typeof(int), oldType: "int", oldNullable: true);
    }
}
