using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261004130000_AddFarmerIdentityLinkAndTokenVersion")]
public partial class AddFarmerIdentityLinkAndTokenVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "GrowerId",
            table: "Users",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TokenVersion",
            table: "Users",
            type: "int",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.CreateIndex(
            name: "IX_Users_GrowerId",
            table: "Users",
            column: "GrowerId",
            unique: true,
            filter: "[GrowerId] IS NOT NULL");

        migrationBuilder.AddForeignKey(
            name: "FK_Users_Growers_GrowerId",
            table: "Users",
            column: "GrowerId",
            principalTable: "Growers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Users_Growers_GrowerId", table: "Users");
        migrationBuilder.DropIndex(name: "IX_Users_GrowerId", table: "Users");
        migrationBuilder.DropColumn(name: "GrowerId", table: "Users");
        migrationBuilder.DropColumn(name: "TokenVersion", table: "Users");
    }
}
