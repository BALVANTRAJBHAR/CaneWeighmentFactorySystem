using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using CaneFactory.Infrastructure.Persistence;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>Persists explicit operator intent; runtime state remains process-local and is never trusted after Stop/Disconnect.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260905090000_AddWeighingOperationalState")]
public partial class AddWeighingOperationalState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DesiredConnectionState",
            table: "WeighingDevices",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "Disconnected");
        migrationBuilder.AddColumn<bool>(
            name: "DesiredReaderRunning",
            table: "WeighingDevices",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DesiredConnectionState", table: "WeighingDevices");
        migrationBuilder.DropColumn(name: "DesiredReaderRunning", table: "WeighingDevices");
    }
}
