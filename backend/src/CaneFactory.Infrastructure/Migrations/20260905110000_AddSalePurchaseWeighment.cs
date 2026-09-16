using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using CaneFactory.Infrastructure.Persistence;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260905110000_AddSalePurchaseWeighment")]
public partial class AddSalePurchaseWeighment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SalePurchases",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false),
                ItemId = table.Column<int>(type: "int", nullable: false),
                PartyId = table.Column<int>(type: "int", nullable: false),
                VehicleTypeId = table.Column<int>(type: "int", nullable: false),
                VehicleNumber = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                DriverName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Remark = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ScaleReadingTareKg = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                TareWeightQuintal = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                TareDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                TareByUserId = table.Column<int>(type: "int", nullable: false),
                TareByUserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ScaleReadingGrossKg = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                GrossWeightQuintal = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                FinalWeightQuintal = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                GrossDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                GrossByUserId = table.Column<int>(type: "int", nullable: true),
                GrossByUserName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Rate = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                Amount = table.Column<decimal>(type: "decimal(14,2)", nullable: true),
                WeighmentStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                CancelReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                TarePrintCount = table.Column<int>(type: "int", nullable: false),
                GrossPrintCount = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                Status = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalePurchases", x => x.Id);
                table.ForeignKey("FK_SalePurchases_Items_ItemId", x => x.ItemId, "Items", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SalePurchases_Parties_PartyId", x => x.PartyId, "Parties", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SalePurchases_VehicleTypes_VehicleTypeId", x => x.VehicleTypeId, "VehicleTypes", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateTable(
            name: "SalePurchaseImages",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                SalePurchaseId = table.Column<int>(type: "int", nullable: false),
                CameraId = table.Column<int>(type: "int", nullable: false),
                CaptureStage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ImageName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                FileHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CapturedBy = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_SalePurchaseImages", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_SalePurchases_WeighmentStatus", table: "SalePurchases", column: "WeighmentStatus");
        migrationBuilder.CreateIndex(name: "IX_SalePurchases_TareDateTime", table: "SalePurchases", column: "TareDateTime");
        migrationBuilder.CreateIndex(name: "IX_SalePurchases_GrossDateTime", table: "SalePurchases", column: "GrossDateTime");
        migrationBuilder.CreateIndex(name: "IX_SalePurchases_PartyId_VehicleNumber", table: "SalePurchases", columns: new[] { "PartyId", "VehicleNumber" });
        migrationBuilder.CreateIndex(name: "IX_SalePurchaseImages_SalePurchaseId", table: "SalePurchaseImages", column: "SalePurchaseId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SalePurchaseImages");
        migrationBuilder.DropTable(name: "SalePurchases");
    }
}
