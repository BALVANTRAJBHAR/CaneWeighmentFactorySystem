using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260905113000_AddSalePurchaseSmsRecipients")]
public partial class AddSalePurchaseSmsRecipients : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "SalePurchaseRecipients", table: "SmsConfigs", type: "nvarchar(max)", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "SalePurchaseRecipients", table: "SmsConfigs");
}
