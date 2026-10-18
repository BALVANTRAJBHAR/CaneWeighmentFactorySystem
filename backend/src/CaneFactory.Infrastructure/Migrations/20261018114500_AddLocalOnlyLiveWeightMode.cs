using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261018114500_AddLocalOnlyLiveWeightMode")]
public partial class AddLocalOnlyLiveWeightMode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>(
            name: "LiveWeightOnlyOnDigitizerPc", table: "WeightRules",
            type: "bit", nullable: false, defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "LiveWeightOnlyOnDigitizerPc", table: "WeightRules");
}
