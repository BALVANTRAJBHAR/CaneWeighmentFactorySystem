using System.Reflection;
using CaneFactory.API.Controllers;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Weighing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

// Isolated SQLite only: no factory database, hardware, SMS or print operations.
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var services = new ServiceCollection();
services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
await db.Database.EnsureCreatedAsync();
db.WeightRules.Add(new WeightRuleConfig { MinimumWeightQuintal = 0, VehicleReweighCooldownMinutes = 0 });
await db.SaveChangesAsync();
var weighing = new WeighingService(provider.GetRequiredService<IServiceScopeFactory>(),
    new NoBroadcast(), NullLogger<WeighingService>.Instance);
var controllers = new (object Controller, string Eligibility)[]
{
    (new WeighmentController(db, null!, null!, null!, null!, null!, null!, weighing,
        NullLogger<WeighmentController>.Instance), "CanStartNewCaneGrossAsync"),
    (new SalePurchaseWeighmentController(db, null!, null!, null!, weighing, null!, null!, null!,
        NullLogger<SalePurchaseWeighmentController>.Instance), "CanStartNewSaleTareAsync")
};
int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
MethodInfo Method(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
async Task Mark(object target, decimal kg)
{
    await (Task)Method(target, "MarkPlatformClearRequiredAsync").Invoke(target, new object[] { kg })!;
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
}
async Task<string?> Eligibility(object target, string name) =>
    await (Task<string?>)Method(target, name).Invoke(target, new object[] { "TEST1234", DateTime.UtcNow })!;
async Task<string> LockValue() => (await db.SystemSettings.AsNoTracking().SingleAsync()).Value;
void Reading(decimal kg) => weighing.ReceiveRemoteReading("Test bridge", kg, true, "READING", null);

foreach (var (controller, eligibility) in controllers)
{
    var name = controller.GetType().Name;
    Reading(0);
    Check(await (Task<string?>)Method(controller, "MinimumWeightErrorAsync")
        .Invoke(controller, new object[] { 0m, true })! == null, name + " minimum zero permits zero");
    await Mark(controller, 0);
    Check(await LockValue() == "0", name + " zero save leaves platform clear");
    Check(await Eligibility(controller, eligibility) == null, name + " repeated zero allowed");
    await Mark(controller, 0);
    Reading(60);
    Check(await Eligibility(controller, eligibility) == null, name + " zero-save then 60kg allowed");
    await Mark(controller, 60);
    Check(await Eligibility(controller, eligibility) != null, name + " nonzero save still requires clear");
    weighing.ReceiveRemoteReading("Test bridge", 0, false, "DISCONNECTED", null);
    Check(await LockValue() == "1", name + " disconnect cannot release platform lock");
    Check(!weighing.TryGetUsableWeight(out _, out _), name + " disconnected zero cannot be saved");

    Reading(0);
    var released = false;
    for (int i = 0; i < 100; i++)
    {
        if (await LockValue() == "0") { released = true; break; }
        await Task.Delay(10);
    }
    Check(released, name + " real bridge zero releases persisted lock");
    db.ChangeTracker.Clear();
    Reading(60);
    Check(await Eligibility(controller, eligibility) == null, name + " next load after zero allowed");

    // Simulate an old persisted lock while the platform is already at zero.
    Reading(0);
    await Mark(controller, 60);
    Check(await Eligibility(controller, eligibility) == null, name + " fresh zero repairs old lock at save");
    await Mark(controller, 0);
    Check(await LockValue() == "0", name + " zero save does not re-arm repaired lock");
}
Console.WriteLine($"PASS: {checks} platform-clear regression checks.");

sealed class NoBroadcast : ILiveWeightBroadcaster
{
    public Task BroadcastAsync(LiveWeightDto dto) => Task.CompletedTask;
}
