using System;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CaneFactory.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260927120000_AddAndroidSimSmsGateway")]
public partial class AddAndroidSimSmsGateway : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("ProviderType", "SmsConfigs", type: "nvarchar(24)", maxLength: 24,
            nullable: false, defaultValue: "HTTP");
        migrationBuilder.AddColumn<string>("ConfigurationName", "SmsConfigs", type: "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<string>("AndroidDeviceId", "SmsConfigs", type: "nvarchar(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("AndroidSimSlot", "SmsConfigs", type: "nvarchar(16)", maxLength: 16,
            nullable: false, defaultValue: "DEFAULT");
        migrationBuilder.AddColumn<int>("AndroidPollIntervalSeconds", "SmsConfigs", type: "int", nullable: false, defaultValue: 5);
        migrationBuilder.AddColumn<bool>("AndroidCredentialVerified", "SmsConfigs", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTime>("AndroidLastVerifiedAt", "SmsConfigs", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("AndroidLastHeartbeatAt", "SmsConfigs", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<string>("AndroidConnectionStatus", "SmsConfigs", type: "nvarchar(32)", maxLength: 32,
            nullable: false, defaultValue: "NOT_PAIRED");

        migrationBuilder.AddColumn<string>("ProviderType", "SmsLogs", type: "nvarchar(24)", maxLength: 24,
            nullable: false, defaultValue: "HTTP");
        migrationBuilder.AddColumn<string>("DeviceId", "SmsLogs", type: "nvarchar(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("SimSlot", "SmsLogs", type: "nvarchar(16)", maxLength: 16, nullable: true);
        migrationBuilder.AddColumn<DateTime>("PickedAt", "SmsLogs", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("FailedAt", "SmsLogs", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<string>("ProviderMessageId", "SmsLogs", type: "nvarchar(max)", nullable: true);

        migrationBuilder.CreateTable(
            name: "AndroidSmsGatewayDevices",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                DeviceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ConfigurationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                ApiKeyHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                ApiKeySalt = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                SimSlot = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "DEFAULT"),
                PollIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                LastHeartbeatAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastSeenIp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                PairedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<bool>(type: "bit", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_AndroidSmsGatewayDevices", x => x.Id));

        migrationBuilder.CreateIndex("IX_AndroidSmsGatewayDevices_DeviceId", "AndroidSmsGatewayDevices", "DeviceId",
            unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_SmsLogs_ProviderType_DeviceId_Status", "SmsLogs",
            new[] { "ProviderType", "DeviceId", "Status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AndroidSmsGatewayDevices");
        migrationBuilder.DropIndex("IX_SmsLogs_ProviderType_DeviceId_Status", "SmsLogs");
        foreach (var column in new[] { "ProviderType", "DeviceId", "SimSlot", "PickedAt", "FailedAt", "ProviderMessageId" })
            migrationBuilder.DropColumn(column, "SmsLogs");
        foreach (var column in new[] { "ProviderType", "ConfigurationName", "AndroidDeviceId", "AndroidSimSlot",
            "AndroidPollIntervalSeconds", "AndroidCredentialVerified", "AndroidLastVerifiedAt", "AndroidLastHeartbeatAt",
            "AndroidConnectionStatus" })
            migrationBuilder.DropColumn(column, "SmsConfigs");
    }
}
