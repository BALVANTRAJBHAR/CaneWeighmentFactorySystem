using System.Security.Cryptography;
using CaneFactory.API.Services;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
await using var db = new AppDbContext(options);
await db.Database.EnsureCreatedAsync();

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

var adminRole = new Role { Name = "Admin", IsSystem = true };
var admin = new User { Username = "admin", FullName = "Approval Admin", Mobile = "9999999999", PasswordHash = "test" };
var reason = new RateReasonMaster { ReasonName = "Quality deduction" };
db.AddRange(adminRole, admin, reason);
await db.SaveChangesAsync();
db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });
await db.SaveChangesAsync();

var service = new RateOverrideService(db);
var unchanged = await service.ValidateAndStageAsync("CANE", 10, 400m, 400m, null, null, null, null, 44);
Check(unchanged == 400m, "Unchanged master rate must not require approval evidence");

try
{
    await service.ValidateAndStageAsync("CANE", 10, 400m, 401m, admin.Id, reason.Id, null, "x", 44);
    throw new InvalidOperationException("Above-master rate was accepted");
}
catch (RateOverrideValidationException ex)
{
    Check(ex.Message.Contains("cannot exceed"), "Above-master rate must be rejected with a clear message");
}

try
{
    await service.ValidateAndStageAsync("CANE", 10, 400m, 390m, admin.Id, reason.Id, null, null, 44);
    throw new InvalidOperationException("Lower rate without evidence was accepted");
}
catch (RateOverrideValidationException ex)
{
    Check(ex.Message.Contains("evidence"), "Lower rate must require evidence");
}

var evidenceBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
var evidencePath = Path.Combine(Path.GetTempPath(), $"rate-override-{Guid.NewGuid():N}.jpg");
await File.WriteAllBytesAsync(evidencePath, evidenceBytes);
try
{
    var evidence = new RateOverrideEvidence
    {
        Token = Guid.NewGuid().ToString("N"), TransactionType = "CANE", TransactionId = 10,
        Source = "UPLOAD", ImageName = Path.GetFileName(evidencePath), FilePath = evidencePath,
        FileHash = Convert.ToHexString(SHA256.HashData(evidenceBytes)), ExpiresAt = DateTime.UtcNow.AddMinutes(10), CreatedBy = 44
    };
    db.RateOverrideEvidences.Add(evidence);
    await db.SaveChangesAsync();

    var approved = await service.ValidateAndStageAsync("CANE", 10, 400m, 390m, admin.Id, reason.Id,
        "Low recovery", evidence.Token, 44);
    await db.SaveChangesAsync();
    Check(approved == 390m, "Approved lower rate must be returned");
    var saved = await db.WeighmentRateOverrides.SingleAsync();
    Check(saved.MasterRate == 400m && saved.ApprovedRate == 390m, "Master and approved rates must both be audited");
    Check(saved.ApprovedByUserId == admin.Id && saved.RateReasonId == reason.Id, "Admin and reason must be audited");
    Check((await db.RateOverrideEvidences.SingleAsync()).ConsumedAt != null, "Evidence token must be consumed once");
}
finally
{
    File.Delete(evidencePath);
}

Console.WriteLine($"PASS: {checks} rate-override security regression checks.");
