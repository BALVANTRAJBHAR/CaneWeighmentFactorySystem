using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261014110000_RestrictWeighmentSaveToBridgeHost")]
public partial class RestrictWeighmentSaveToBridgeHost : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>(
            name: "RestrictWeighmentSaveToBridgeHost", table: "WeightRules",
            type: "bit", nullable: false, defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "RestrictWeighmentSaveToBridgeHost", table: "WeightRules");
}
