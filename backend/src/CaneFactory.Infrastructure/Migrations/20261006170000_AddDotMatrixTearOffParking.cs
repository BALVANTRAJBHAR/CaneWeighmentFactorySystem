using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261006170000_AddDotMatrixTearOffParking")]
public partial class AddDotMatrixTearOffParking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "DotMatrixTearOffParkingEnabled",
            table: "PrintConfigs",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<int>(
            name: "DotMatrixTearOffFeedLines",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 12);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DotMatrixTearOffParkingEnabled", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "DotMatrixTearOffFeedLines", table: "PrintConfigs");
    }
}
