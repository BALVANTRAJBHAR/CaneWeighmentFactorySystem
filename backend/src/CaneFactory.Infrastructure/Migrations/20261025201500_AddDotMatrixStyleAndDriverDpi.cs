using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261025201500_AddDotMatrixStyleAndDriverDpi")]
public partial class AddDotMatrixStyleAndDriverDpi : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DotMatrixFontStyle",
            table: "PrintConfigs",
            type: "nvarchar(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Sans");

        migrationBuilder.AddColumn<int>(
            name: "DotMatrixDriverRenderDpi",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 240);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DotMatrixFontStyle", table: "PrintConfigs");
        migrationBuilder.DropColumn(name: "DotMatrixDriverRenderDpi", table: "PrintConfigs");
    }
}
