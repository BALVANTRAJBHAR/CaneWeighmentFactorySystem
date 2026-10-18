using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

/// <summary>Adds an independent per-camera switch for automatic cane/sale weighment evidence.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261018100000_AddWeighmentCameraPurpose")]
public partial class AddWeighmentCameraPurpose : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // True preserves the pre-upgrade behaviour. Developer can then turn off
        // weighment capture for cameras that are used only for another purpose.
        migrationBuilder.AddColumn<bool>(
            name: "WeighmentCaptureEnabled",
            table: "Cameras",
            type: "bit",
            nullable: false,
            defaultValue: true);

        // Preserve today's working behaviour during upgrade: copy the currently
        // selected payment camera once, then both settings can be changed independently.
        migrationBuilder.Sql(@"
INSERT INTO SystemSettings ([Key], [Value], [Description])
SELECT N'RateEvidenceCameraId', [Value], N'Dedicated camera for cane/sale rate-edit evidence'
FROM SystemSettings
WHERE [Key] = N'PaymentEvidenceCameraId'
  AND NOT EXISTS (SELECT 1 FROM SystemSettings WHERE [Key] = N'RateEvidenceCameraId');");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM SystemSettings WHERE [Key] = N'RateEvidenceCameraId';");
        migrationBuilder.DropColumn(name: "WeighmentCaptureEnabled", table: "Cameras");
    }
}
