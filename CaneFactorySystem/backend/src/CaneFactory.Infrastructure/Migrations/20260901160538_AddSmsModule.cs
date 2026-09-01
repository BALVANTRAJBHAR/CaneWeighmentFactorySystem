using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaneFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "EventCode",
                table: "SmsTemplates",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "SmsTemplates",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "SmsConfigs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestBodyTemplate",
                table: "SmsConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestContentType",
                table: "SmsConfigs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResponseSuccessPath",
                table: "SmsConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseSuccessValue",
                table: "SmsConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SmsLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    GrowerId = table.Column<int>(type: "int", nullable: false),
                    MobileNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferenceId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TemplateId = table.Column<int>(type: "int", nullable: true),
                    MessageText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmsTemplates_EventCode_Language",
                table: "SmsTemplates",
                columns: new[] { "EventCode", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmsLogs_EventCode_ReferenceId",
                table: "SmsLogs",
                columns: new[] { "EventCode", "ReferenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmsLogs_GrowerId",
                table: "SmsLogs",
                column: "GrowerId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsLogs_Status",
                table: "SmsLogs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmsLogs");

            migrationBuilder.DropIndex(
                name: "IX_SmsTemplates_EventCode_Language",
                table: "SmsTemplates");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "SmsTemplates");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "SmsConfigs");

            migrationBuilder.DropColumn(
                name: "RequestBodyTemplate",
                table: "SmsConfigs");

            migrationBuilder.DropColumn(
                name: "RequestContentType",
                table: "SmsConfigs");

            migrationBuilder.DropColumn(
                name: "ResponseSuccessPath",
                table: "SmsConfigs");

            migrationBuilder.DropColumn(
                name: "ResponseSuccessValue",
                table: "SmsConfigs");

            migrationBuilder.AlterColumn<string>(
                name: "EventCode",
                table: "SmsTemplates",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
