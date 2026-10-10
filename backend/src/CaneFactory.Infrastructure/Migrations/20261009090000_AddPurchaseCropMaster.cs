using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261009090000_AddPurchaseCropMaster")]
public partial class AddPurchaseCropMaster : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Crops",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CropName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                CropNameHi = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<bool>(type: "bit", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Crops", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_Crops_CropName", table: "Crops", column: "CropName", unique: true);

        migrationBuilder.AddColumn<int>(
            name: "CropId",
            table: "Purchases",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(name: "IX_Purchases_CropId", table: "Purchases", column: "CropId");
        migrationBuilder.AddForeignKey(
            name: "FK_Purchases_Crops_CropId",
            table: "Purchases",
            column: "CropId",
            principalTable: "Crops",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Purchases_Crops_CropId", table: "Purchases");
        migrationBuilder.DropIndex(name: "IX_Purchases_CropId", table: "Purchases");
        migrationBuilder.DropColumn(name: "CropId", table: "Purchases");
        migrationBuilder.DropTable(name: "Crops");
    }
}
