using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261007100000_AllowHalfLineTearOffFeed")]
public partial class AllowHalfLineTearOffFeed : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "DotMatrixTearOffFeedLines",
            table: "PrintConfigs",
            type: "decimal(5,1)",
            nullable: false,
            defaultValue: 12m,
            oldClrType: typeof(int),
            oldType: "int",
            oldDefaultValue: 12);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<int>(
            name: "DotMatrixTearOffFeedLines",
            table: "PrintConfigs",
            type: "int",
            nullable: false,
            defaultValue: 12,
            oldClrType: typeof(decimal),
            oldType: "decimal(5,1)",
            oldDefaultValue: 12m);
    }
}
