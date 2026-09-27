using CaneFactory.API.Services;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Sms;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var credentials = new AndroidGatewayCredentialService();
var (hash, salt) = credentials.Hash("correct-long-device-key");
Check(credentials.Verify("correct-long-device-key", hash, salt), "correct key verifies");
Check(!credentials.Verify("wrong-device-key", hash, salt), "wrong key is rejected");
Check(hash != "correct-long-device-key", "plain key is not persisted");

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
await using var db = new AppDbContext(options);
Check(db.Database.GetMigrations().Contains("20260927120000_AddAndroidSimSmsGateway"),
    "Android SIM EF migration is discoverable");
await db.Database.EnsureCreatedAsync();
db.SmsConfigs.Add(new SmsConfig
{
    ProviderType = "ANDROID_SIM",
    ProviderName = "Android SIM",
    Enabled = true,
    AndroidCredentialVerified = true,
    AndroidDeviceId = "PHONE-01",
    AndroidSimSlot = "SIM2"
});
await db.SaveChangesAsync();

var service = new SmsService(db);
Check(await service.QueueRawAsync("TEST", null, null, "9876543210", "TEST-1", "Gateway test"),
    "first queue insert succeeds");
Check(!await service.QueueRawAsync("TEST", null, null, "9876543210", "TEST-1", "Gateway test"),
    "same event/reference is not queued twice");
var row = await db.SmsLogs.SingleAsync();
Check(row.ProviderType == "ANDROID_SIM" && row.DeviceId == "PHONE-01" && row.SimSlot == "SIM2",
    "queue snapshots provider, device and SIM");

var firstClaim = await db.SmsLogs.Where(x => x.Id == row.Id && x.Status == "QUEUED")
    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "PROCESSING")
        .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1));
var duplicateClaim = await db.SmsLogs.Where(x => x.Id == row.Id && x.Status == "QUEUED")
    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "PROCESSING"));
Check(firstClaim == 1 && duplicateClaim == 0, "database transition prevents duplicate claim");

Console.WriteLine("Android SIM gateway regression checks passed.");

static void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException($"FAILED: {label}");
    Console.WriteLine($"PASS: {label}");
}
