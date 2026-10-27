using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261025190000_AddDotMatrixFontWeight")]
public partial class AddDotMatrixFontWeight : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DotMatrixFontWeight",
            table: "PrintConfigs",
            type: "nvarchar(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Regular");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DotMatrixFontWeight", table: "PrintConfigs");
    }
}
