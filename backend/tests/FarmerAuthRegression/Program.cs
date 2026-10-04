using System.Text.Json;
using CaneFactory.API.Auth;
using CaneFactory.API.Controllers;
using CaneFactory.API.Services;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

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

Check(FarmerAccountService.BuildTemporaryPassword("Rohit Kumar", "9876543210") == "ROHI9876543210",
    "Temporary password must use the first four non-space uppercase name characters plus mobile");

var village = new Village { Id = 101, VillageName = "Test Village", ZoneId = 1 };
var farmerRole = new Role { Name = "Farmer", IsSystem = true };
var grower = new Grower
{
    Id = 100001,
    Village = village,
    VillageId = village.Id,
    GrowerSequence = 1,
    GrowerCode = "101/1",
    GrowerName = "Rohit Kumar",
    FatherName = "Test Father",
    Mobile = "9876543210"
};
// SQLite test model still enforces Village -> Zone, so keep only the required entities attached
// and temporarily disable FK checks while building this isolated ownership fixture.
await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF;");
db.AddRange(village, farmerRole, grower);
await db.SaveChangesAsync();
await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;");

var cache = new MemoryCache(new MemoryCacheOptions());
var accountService = new FarmerAccountService(db, new NoopAudit(),
    new UserStateService(db, cache), NullLogger<FarmerAccountService>.Instance);
var result = await accountService.EnsureForGrowerAsync(grower, identityChanged: true);
var user = await db.Users.Include(u => u.UserRoles).SingleAsync();
Check(result.Created && user.GrowerId == grower.Id, "Farmer identity must be linked by GrowerId");
Check(user.MustChangePassword, "Provisioned Farmer must be forced to change the password");
Check(new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, "ROHI9876543210")
      != PasswordVerificationResult.Failed, "Temporary password must be stored only as a salted hash");
Check(user.UserRoles.Single().RoleId == farmerRole.Id, "Provisioned identity must receive Farmer role");

await accountService.EnsureForGrowerAsync(grower);
Check(await db.Users.CountAsync() == 1, "Synchronization must be idempotent");

// Deliberately add a legacy duplicate-mobile grower after provisioning. The authenticated
// ownership link must still select Grower 100001, proving mobile is not an authorization key.
db.Growers.Add(new Grower
{
    Id = 100002,
    VillageId = village.Id,
    GrowerSequence = 2,
    GrowerCode = "101/2",
    GrowerName = "Other Farmer",
    FatherName = "Other Father",
    Mobile = grower.Mobile
});
await db.SaveChangesAsync();

var dashboard = new FarmerController(db, new TestCurrentUser(user.Id), null!);
var dashboardResult = (OkObjectResult)await dashboard.Dashboard();
var dashboardJson = JsonSerializer.Serialize(dashboardResult.Value);
Check(dashboardJson.Contains("100001"), "Dashboard must resolve the authenticated user's linked GrowerId");
Check(!dashboardJson.Contains("100002"), "Duplicate mobile must never cross the immutable GrowerId ownership boundary");
Check(!dashboardJson.Contains("9876543210"), "Dashboard must not expose the full registered mobile number");
Check(dashboardJson.Contains("XXXXXX3210"), "Dashboard must expose only a masked mobile number");

Console.WriteLine($"PASS: {checks} farmer identity and ownership regression checks.");

file sealed class NoopAudit : IAuditService
{
    public Task LogAsync(string action, string module, string? entity = null, string? entityId = null,
        object? oldValue = null, object? newValue = null, bool success = true, string? failureReason = null)
        => Task.CompletedTask;
}

file sealed class TestCurrentUser(int userId) : ICurrentUser
{
    public int? UserId => userId;
    public string? Username => "farmer-100001";
    public string? Role => "Farmer";
    public string? Ip => "127.0.0.1";
    public string? Device => "regression";
    public bool HasPermission(string code) => code == "Dashboard.View";
}
