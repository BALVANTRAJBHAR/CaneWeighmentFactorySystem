using System.Security.Claims;
using System.Text.Json;
using CaneFactory.API.Controllers;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Isolated SQLite only: no factory database, hardware or external service operations.
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

var developerRole = new Role { Name = "Developer", IsSystem = true };
var adminRole = new Role { Name = "Admin", IsSystem = true };
var developer = new User
{
    Username = "developer", FullName = "System Developer", Mobile = "9999999999",
    PasswordHash = "test", Status = true, MustChangePassword = false
};
var admin = new User
{
    Username = "admin1", FullName = "Admin One", Mobile = "8888888888",
    PasswordHash = "test", Status = true, MustChangePassword = false
};
db.AddRange(developerRole, adminRole, developer, admin);
await db.SaveChangesAsync();
db.UserRoles.AddRange(
    new UserRole { UserId = developer.Id, RoleId = developerRole.Id },
    new UserRole { UserId = admin.Id, RoleId = adminRole.Id });
db.SystemSettings.Add(new SystemSetting { Key = "WeighbridgePlatformClearRequired", Value = "1" });
await db.SaveChangesAsync();

var userController = new UsersController(db, null!, null!)
{
    ControllerContext = DeveloperControllerContext(developer.Id)
};
var list = (OkObjectResult)await userController.List(null, true, 1, 50);
var listJson = JsonSerializer.Serialize(list.Value);
Check(listJson.Contains("developer", StringComparison.OrdinalIgnoreCase),
    "Developer identity must appear for a Developer caller");
Check(listJson.Contains("admin1", StringComparison.OrdinalIgnoreCase),
    "Normal users must remain visible in User Management list");

userController.ControllerContext = AdminControllerContext(admin.Id);
var adminList = (OkObjectResult)await userController.List(null, true, 1, 50);
var adminListJson = JsonSerializer.Serialize(adminList.Value);
Check(!adminListJson.Contains("developer", StringComparison.OrdinalIgnoreCase),
    "Developer identity must remain hidden from an Admin caller");
Check(adminListJson.Contains("admin1", StringComparison.OrdinalIgnoreCase),
    "Normal users must remain visible to an Admin caller");
userController.ControllerContext = DeveloperControllerContext(developer.Id);

var createDeveloper = await userController.Create(new CreateUserRequest
{
    Username = "anotherdev",
    FullName = "Another Developer",
    Mobile = "7777777777",
    Email = "another@example.com",
    TemporaryPassword = "Temporary@123",
    RoleIds = new List<int> { developerRole.Id }
});
Check(createDeveloper is BadRequestObjectResult,
    "Developer role assignment must be blocked even for a Developer caller");

var audit = new RecordingAudit();
var configController = new ConfigController(db, audit, new CurrentDeveloper(developer.Id), null!)
{
    ControllerContext = DeveloperControllerContext(developer.Id)
};
var badReason = await configController.UnlockWeighbridgePlatform(
    new Dictionary<string, string> { ["reason"] = "no" });
Check(badReason is BadRequestObjectResult, "Short safety override reason must be rejected");
Check((await db.SystemSettings.SingleAsync(s => s.Key == "WeighbridgePlatformClearRequired")).Value == "1",
    "Rejected override must leave platform locked");

var unlocked = await configController.UnlockWeighbridgePlatform(
    new Dictionary<string, string> { ["reason"] = "Digitizer restarted after the vehicle left" });
Check(unlocked is OkObjectResult, "Developer safety override must succeed with confirmation reason");
Check((await db.SystemSettings.SingleAsync(s => s.Key == "WeighbridgePlatformClearRequired")).Value == "0",
    "Developer safety override must release persisted platform lock");
Check(audit.Action == "SafetyOverride", "Developer safety override must be audited");

Console.WriteLine($"PASS: {checks} admin/developer safety regression checks.");

static ControllerContext DeveloperControllerContext(int userId)
{
    var identity = new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        new Claim(ClaimTypes.Name, "developer"),
        new Claim(ClaimTypes.Role, "Developer")
    }, "Regression");
    return new ControllerContext
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
    };
}

static ControllerContext AdminControllerContext(int userId)
{
    var identity = new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        new Claim(ClaimTypes.Name, "admin1"),
        new Claim(ClaimTypes.Role, "Admin")
    }, "Regression");
    return new ControllerContext
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
    };
}

sealed class CurrentDeveloper(int userId) : ICurrentUser
{
    public int? UserId => userId;
    public string? Username => "developer";
    public string? Role => "Developer";
    public string? Ip => "127.0.0.1";
    public string? Device => "Regression";
    public bool HasPermission(string code) => true;
}

sealed class RecordingAudit : IAuditService
{
    public string? Action { get; private set; }
    public Task LogAsync(string action, string module, string? entity = null, string? entityId = null,
        object? oldValue = null, object? newValue = null, bool success = true, string? failureReason = null)
    {
        Action = action;
        return Task.CompletedTask;
    }
}
