using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentId",
                table: "LoanRecoveries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    CameraId = table.Column<int>(type: "int", nullable: false),
                    ImageName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CapturedBy = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentImages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    AdviceNumber = table.Column<int>(type: "int", nullable: false),
                    GrowerId = table.Column<int>(type: "int", nullable: false),
                    GrowerCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    VillageId = table.Column<int>(type: "int", nullable: false),
                    TotalPurchaseAmount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    LoanDeductedAmount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    NetPayableAmount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    PaymentModeId = table.Column<int>(type: "int", nullable: false),
                    TransactionRefNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidByUserId = table.Column<int>(type: "int", nullable: false),
                    PaidByUserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    PrintCount = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Growers_GrowerId",
                        column: x => x.GrowerId,
                        principalTable: "Growers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_PaymentModes_PaymentModeId",
                        column: x => x.PaymentModeId,
                        principalTable: "PaymentModes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentPurchases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    PurchaseId = table.Column<int>(type: "int", nullable: false),
                    PurchaseAmountAtPayment = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentPurchases_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentPurchases_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoanRecoveries_PaymentId",
                table: "LoanRecoveries",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentImages_PaymentId",
                table: "PaymentImages",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPurchases_PaymentId",
                table: "PaymentPurchases",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPurchases_PurchaseId",
                table: "PaymentPurchases",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_AdviceNumber",
                table: "Payments",
                column: "AdviceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_GrowerCode",
                table: "Payments",
                column: "GrowerCode");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_GrowerId",
                table: "Payments",
                column: "GrowerId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentModeId",
                table: "Payments",
                column: "PaymentModeId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentStatus",
                table: "Payments",
                column: "PaymentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SeasonId",
                table: "Payments",
                column: "SeasonId");

            migrationBuilder.AddForeignKey(
                name: "FK_LoanRecoveries_Payments_PaymentId",
                table: "LoanRecoveries",
                column: "PaymentId",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoanRecoveries_Payments_PaymentId",
                table: "LoanRecoveries");

            migrationBuilder.DropTable(
                name: "PaymentImages");

            migrationBuilder.DropTable(
                name: "PaymentPurchases");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_LoanRecoveries_PaymentId",
                table: "LoanRecoveries");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "LoanRecoveries");
        }
    }
}
