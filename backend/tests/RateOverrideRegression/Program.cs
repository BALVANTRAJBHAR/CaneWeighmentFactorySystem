using System.Security.Cryptography;
using CaneFactory.API.Controllers;
using CaneFactory.API.Services;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Camera;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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
var configuredStorage = Path.Combine(Path.GetTempPath(), $"rate-edit-storage-{Guid.NewGuid():N}");
var resolvedRoot = RateEditImagePathResolver.ResolveRoot(configuredStorage);
Check(resolvedRoot.EndsWith(Path.Combine("WeighmentImage", "RateEditImage"), StringComparison.OrdinalIgnoreCase),
    "Rate evidence root must be inside WeighmentImage/RateEditImage");
Check(RateEditImagePathResolver.BuildStem("CANE", 23, 100001, "TARE") == "100001-TARE-23-RATEEDIT",
    "Cane rate evidence filename must identify the Tare stage instead of payment advice");
Check(RateEditImagePathResolver.BuildStem("SALE", 9, 4, "GROSS") == "4-GROSS-9-RATEEDIT",
    "Sale rate evidence filename must identify the Gross stage");
Check(RateEditImagePathResolver.IsUnderRoot(Path.Combine(resolvedRoot, "Cane", "image.jpg"), resolvedRoot),
    "Rate evidence path guard must permit only descendants of the configured root");
Check(!RateEditImagePathResolver.IsUnderRoot(Path.Combine(Path.GetTempPath(), "outside.jpg"), resolvedRoot),
    "Rate evidence path guard must reject files outside the configured root");
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
var evidenceFolder = Path.Combine(resolvedRoot, "Cane", DateTime.Now.ToString("yyyy-MM-dd"));
Directory.CreateDirectory(evidenceFolder);
var evidencePath = Path.Combine(evidenceFolder, "100001-0-10-RATEEDIT.jpg");
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

    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Storage:ImageRoot"] = configuredStorage
    }).Build();
    var images = new ImagesController(db, new ImageReader(), configuration)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
    Check(await images.Search("rate-edit", "10") is OkObjectResult,
        "View Images must search committed rate evidence by transaction ID");
    Check(await images.RateEditFile(evidence.Id) is FileContentResult,
        "View Images must serve committed rate evidence after path/hash checks");
}
finally
{
    if (Directory.Exists(configuredStorage)) Directory.Delete(configuredStorage, recursive: true);
}

Console.WriteLine($"PASS: {checks} rate-override security regression checks.");

sealed class ImageReader : ICurrentUser
{
    public int? UserId => 1;
    public string? Username => "image-reader";
    public string? Role => "Admin";
    public string? Ip => "127.0.0.1";
    public string? Device => "Regression";
    public bool HasPermission(string code) => code == "Image.View";
}
