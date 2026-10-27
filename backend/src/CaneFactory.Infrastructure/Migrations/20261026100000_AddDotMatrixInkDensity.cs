using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261026100000_AddDotMatrixInkDensity")]
public partial class AddDotMatrixInkDensity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DotMatrixInkDensity",
            table: "PrintConfigs",
            type: "nvarchar(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Light");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DotMatrixInkDensity", table: "PrintConfigs");
    }
}
