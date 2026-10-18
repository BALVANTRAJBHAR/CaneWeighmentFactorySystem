using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>
/// Rate approval is an explicit developer-controlled user authority, not an
/// implicit consequence of holding the Admin role.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261014090000_AddRateApprovalAuthority")]
public partial class AddRateApprovalAuthority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "CanApproveRateOverrides",
            table: "Users",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CanApproveRateOverrides",
            table: "Users");
    }
}
