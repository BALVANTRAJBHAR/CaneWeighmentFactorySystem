using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261003150000_AddWeighmentRateOverrides")]
public partial class AddWeighmentRateOverrides : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RateReasons",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                ReasonName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<bool>(type: "bit", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_RateReasons", x => x.Id));

        migrationBuilder.CreateTable(
            name: "RateOverrideEvidences",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                TransactionType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                TransactionId = table.Column<int>(type: "int", nullable: false),
                Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ImageName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                FilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<bool>(type: "bit", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_RateOverrideEvidences", x => x.Id));

        migrationBuilder.CreateTable(
            name: "WeighmentRateOverrides",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TransactionType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                TransactionId = table.Column<int>(type: "int", nullable: false),
                MasterRate = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                ApprovedRate = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                ApprovedByUserId = table.Column<int>(type: "int", nullable: false),
                ApprovedByUserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                RateReasonId = table.Column<int>(type: "int", nullable: false),
                RateReasonText = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                EvidenceId = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<bool>(type: "bit", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WeighmentRateOverrides", x => x.Id);
                table.ForeignKey("FK_WeighmentRateOverrides_RateOverrideEvidences_EvidenceId", x => x.EvidenceId,
                    "RateOverrideEvidences", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_WeighmentRateOverrides_RateReasons_RateReasonId", x => x.RateReasonId,
                    "RateReasons", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_WeighmentRateOverrides_Users_ApprovedByUserId", x => x.ApprovedByUserId,
                    "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_RateReasons_ReasonName", "RateReasons", "ReasonName", unique: true);
        migrationBuilder.CreateIndex("IX_RateOverrideEvidences_Token", "RateOverrideEvidences", "Token", unique: true);
        migrationBuilder.CreateIndex("IX_RateOverrideEvidences_TransactionType_TransactionId", "RateOverrideEvidences",
            new[] { "TransactionType", "TransactionId" });
        migrationBuilder.CreateIndex("IX_WeighmentRateOverrides_ApprovedByUserId", "WeighmentRateOverrides", "ApprovedByUserId");
        migrationBuilder.CreateIndex("IX_WeighmentRateOverrides_EvidenceId", "WeighmentRateOverrides", "EvidenceId");
        migrationBuilder.CreateIndex("IX_WeighmentRateOverrides_RateReasonId", "WeighmentRateOverrides", "RateReasonId");
        migrationBuilder.CreateIndex("IX_WeighmentRateOverrides_TransactionType_TransactionId", "WeighmentRateOverrides",
            new[] { "TransactionType", "TransactionId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("WeighmentRateOverrides");
        migrationBuilder.DropTable("RateOverrideEvidences");
        migrationBuilder.DropTable("RateReasons");
    }
}
