using CaneFactory.API.Controllers;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

// Isolated SQLite only: validates public Grower ID generation without touching production data.
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
await using var db = new AppDbContext(options);
await db.Database.EnsureCreatedAsync();
var migrations = db.GetService<IMigrationsAssembly>().Migrations;
if (!migrations.ContainsKey("20261003090000_UseSixDigitGrowerIds"))
    throw new InvalidOperationException("The six-digit Grower ID production migration is not discoverable.");

var zone = new Zone { ZoneCode = "Z1", ZoneName = "Regression Zone" };
db.Zones.Add(zone);
await db.SaveChangesAsync();
var village = new Village { ZoneId = zone.Id, VillageName = "Regression Village" };
db.Villages.Add(village);
await db.SaveChangesAsync();

var controller = new GrowersController(db, new NoAudit(), new TestUser(),
    new SequenceGenerator(db), new NoSecrets(), NullLogger<GrowersController>.Instance);

async Task<int> Create(string name, string mobile)
{
    var result = await controller.Create(new GrowerRequest
    {
        VillageId = village.Id,
        GrowerName = name,
        FatherName = "Regression Father",
        Mobile = mobile,
        AcceptDuplicateWarning = true
    });
    if (result is not OkObjectResult) throw new InvalidOperationException($"Grower create failed: {result.GetType().Name}");
    return await db.Growers.Where(x => x.Mobile == mobile).Select(x => x.Id).SingleAsync();
}

var first = await Create("First Grower", "9000000001");
var second = await Create("Second Grower", "9000000002");
if (first != 100001 || second != 100002)
    throw new InvalidOperationException($"Expected Grower IDs 100001/100002, received {first}/{second}.");

var lookup = await controller.ById(first);
if (lookup is not OkObjectResult)
    throw new InvalidOperationException("Six-digit Grower ID lookup failed.");

Console.WriteLine("PASS: Grower IDs start at 100001, increment continuously, and resolve by ID.");

sealed class TestUser : ICurrentUser
{
    public int? UserId => 1;
    public string? Username => "regression";
    public string? Role => "Developer";
    public string? Ip => "127.0.0.1";
    public string? Device => "Regression";
    public bool HasPermission(string code) => true;
}

sealed class NoAudit : IAuditService
{
    public Task LogAsync(string action, string module, string? entity = null, string? entityId = null,
        object? oldValue = null, object? newValue = null, bool success = true, string? failureReason = null) =>
        Task.CompletedTask;
}

sealed class NoSecrets : ISecretProtector
{
    public string Protect(string plainText) => plainText;
    public string Unprotect(string cipherText) => cipherText;
    public string Hash(string value) => value;
}
