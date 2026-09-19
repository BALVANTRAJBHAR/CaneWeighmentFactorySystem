using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>
/// Makes vehicle identification canonical and enforces one active weighing per vehicle.
/// The filtered unique indexes are deliberately database-side so app restart, digitizer
/// reconnect, or two operator PCs cannot create duplicate active transactions.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260919090000_AddVehicleReweighSafety")]
public partial class AddVehicleReweighSafety : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "VehicleReweighCooldownMinutes",
            table: "WeightRules",
            type: "int",
            nullable: false,
            defaultValue: 30);

        // Normalize historical values for the documented common forms before adding indexes.
        migrationBuilder.Sql("UPDATE [Purchases] SET [VehicleNumber] = UPPER(REPLACE(REPLACE(REPLACE([VehicleNumber], ' ', ''), '-', ''), '/', '')) WHERE [VehicleNumber] IS NOT NULL;");
        migrationBuilder.Sql("UPDATE [SalePurchases] SET [VehicleNumber] = UPPER(REPLACE(REPLACE(REPLACE([VehicleNumber], ' ', ''), '-', ''), '/', '')) WHERE [VehicleNumber] IS NOT NULL;");

        migrationBuilder.AlterColumn<string>(
            name: "VehicleNumber",
            table: "Purchases",
            type: "nvarchar(15)",
            maxLength: 15,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)");

        migrationBuilder.CreateIndex(
            name: "UX_Purchases_ActiveVehicle",
            table: "Purchases",
            column: "VehicleNumber",
            unique: true,
            filter: "[IsDeleted] = 0 AND [GrossTareStatus] = 'GROSS_DONE'");

        migrationBuilder.CreateIndex(
            name: "UX_SalePurchases_ActiveVehicle",
            table: "SalePurchases",
            column: "VehicleNumber",
            unique: true,
            filter: "[IsDeleted] = 0 AND [WeighmentStatus] = 'TARE_PENDING_GROSS'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "UX_Purchases_ActiveVehicle", table: "Purchases");
        migrationBuilder.DropIndex(name: "UX_SalePurchases_ActiveVehicle", table: "SalePurchases");

        migrationBuilder.AlterColumn<string>(
            name: "VehicleNumber",
            table: "Purchases",
            type: "nvarchar(max)",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(15)",
            oldMaxLength: 15);

        migrationBuilder.DropColumn(name: "VehicleReweighCooldownMinutes", table: "WeightRules");
    }
}
