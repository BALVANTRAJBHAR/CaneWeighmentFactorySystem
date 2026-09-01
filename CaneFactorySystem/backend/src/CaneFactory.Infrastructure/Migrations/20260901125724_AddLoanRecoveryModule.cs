using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanRecoveryModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoanTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoanTypeName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_LoanTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Loans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    GrowerId = table.Column<int>(type: "int", nullable: false),
                    GrowerCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    VillageId = table.Column<int>(type: "int", nullable: false),
                    LoanTypeId = table.Column<int>(type: "int", nullable: false),
                    LoanAmount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    RecoveredAmount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    OutstandingAmount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssuedByUserId = table.Column<int>(type: "int", nullable: false),
                    IssuedByUserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LoanStatus = table.Column<string>(type: "nvarchar(450)", nullable: false),
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
                    table.PrimaryKey("PK_Loans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Loans_Growers_GrowerId",
                        column: x => x.GrowerId,
                        principalTable: "Growers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Loans_LoanTypes_LoanTypeId",
                        column: x => x.LoanTypeId,
                        principalTable: "LoanTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Loans_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoanRecoveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LoanId = table.Column<int>(type: "int", nullable: false),
                    GrowerId = table.Column<int>(type: "int", nullable: false),
                    GrowerCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RecoveryAmount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    RecoveryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecoveredByUserId = table.Column<int>(type: "int", nullable: false),
                    RecoveredByUserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecoveryStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_LoanRecoveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanRecoveries_Loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "Loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoanRecoveries_GrowerCode",
                table: "LoanRecoveries",
                column: "GrowerCode");

            migrationBuilder.CreateIndex(
                name: "IX_LoanRecoveries_LoanId",
                table: "LoanRecoveries",
                column: "LoanId");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_GrowerCode",
                table: "Loans",
                column: "GrowerCode");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_GrowerId",
                table: "Loans",
                column: "GrowerId");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_LoanStatus",
                table: "Loans",
                column: "LoanStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_LoanTypeId",
                table: "Loans",
                column: "LoanTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_SeasonId",
                table: "Loans",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanTypes_LoanTypeName",
                table: "LoanTypes",
                column: "LoanTypeName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoanRecoveries");

            migrationBuilder.DropTable(
                name: "Loans");

            migrationBuilder.DropTable(
                name: "LoanTypes");
        }
    }
}
