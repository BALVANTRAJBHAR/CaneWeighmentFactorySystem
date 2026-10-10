using System.Reflection;
using CaneFactory.API.Controllers;
using CaneFactory.API.Services;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Weighing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
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
using var cache = new MemoryCache(new MemoryCacheOptions());
var current = new TestCurrentUser();
var rateOverrides = new RateOverrideService(db);
var controllers = new (object Controller, string Eligibility)[]
{
    (new WeighmentController(db, null!, current, null!, cache, null!, null!, weighing,
        NullLogger<WeighmentController>.Instance, rateOverrides), "CanStartNewCaneGrossAsync"),
    (new SalePurchaseWeighmentController(db, current, null!, null!, weighing, null!, null!, cache,
        NullLogger<SalePurchaseWeighmentController>.Instance, rateOverrides), "CanStartNewSaleTareAsync")
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

// A rejected low-weight request must not consume its idempotency key or enter
// the serializable save path. Repeating the exact same request returns the same
// minimum-weight message for both Sale Tare and Sale Gross.
var rule = await db.WeightRules.SingleAsync();
rule.MinimumWeightQuintal = 0.10m; // 10 kg
rule.Enabled = rule.ApplyToSalePurchase = rule.ApplyToGross = rule.ApplyToTare = true;
await db.SaveChangesAsync();
db.ChangeTracker.Clear();
Reading(0);
var saleController = (SalePurchaseWeighmentController)controllers[1].Controller;
var tareRequest = new SalePurchaseTareSaveRequest { IdempotencyKey = "repeat-low-tare" };
var grossRequest = new SalePurchaseGrossSaveRequest { SalePurchaseId = 999, IdempotencyKey = "repeat-low-gross" };
for (var attempt = 1; attempt <= 2; attempt++)
{
    Check(IsMinimumConflict(await saleController.SaveTare(tareRequest)), $"sale tare low-weight attempt {attempt}");
    Check(IsMinimumConflict(await saleController.SaveGross(grossRequest)), $"sale gross low-weight attempt {attempt}");
}

// UI blocks future dates, and the API independently rejects them so a direct
// request cannot bypass the accounting date rule.
var cashBook = new CashBookController(db, current, new NoAudit());
var tomorrow = DateTime.Today.AddDays(1);
Check(IsBadRequest(await cashBook.ReceiveCash(new CashBookReceiptRequest { EntryDate = tomorrow })),
    "future cash receipt rejected");
Check(IsBadRequest(await cashBook.PayCash(new CashBookPaymentRequest { EntryDate = tomorrow })),
    "future cash payment rejected");

// A daily close withdraws only the server-calculated physical balance and is
// retained as a dedicated immutable ledger row. It cannot be duplicated or
// followed by another same-day manual posting.
Check(IsOk(await cashBook.ReceiveCash(new CashBookReceiptRequest
{
    EntryDate = DateTime.Today, SourceType = "BANK", SourceName = "Opening cash", Amount = 1000m
})), "daily cash-in accepted");
Check(IsOk(await cashBook.PayCash(new CashBookPaymentRequest
{
    EntryDate = DateTime.Today, PaidTo = "Supplier", Amount = 200m, Remarks = "Daily expense"
})), "daily other cash-out accepted");
db.CashBookEntries.Add(new CashBookEntry
{
    EntryDate = DateTime.Today, EntryType = "CASH_OUT", SourceType = "FARMER_PAYMENT",
    SourceName = "Farmer cash payment", Amount = 300m, CreatedBy = 1
});
await db.SaveChangesAsync();
Check(IsOk(await cashBook.CloseDay(new CashBookDayCloseRequest
{
    EntryDate = DateTime.Today, CashTakenBy = "Owner"
})), "daily close accepted");
var closeEntry = await db.CashBookEntries.SingleAsync(x => x.SourceType == "DAY_CLOSING_WITHDRAWAL");
Check(closeEntry.Amount == 500m, "daily close withdraws exact remaining cash");
Check(IsConflict(await cashBook.CloseDay(new CashBookDayCloseRequest
{
    EntryDate = DateTime.Today, CashTakenBy = "Owner"
})), "duplicate daily close rejected");
Check(IsConflict(await cashBook.ReceiveCash(new CashBookReceiptRequest
{
    EntryDate = DateTime.Today, SourceType = "BANK", SourceName = "Late cash", Amount = 1m
})), "posting after daily close rejected");
Console.WriteLine($"PASS: {checks} platform-clear regression checks.");

static bool IsMinimumConflict(Microsoft.AspNetCore.Mvc.IActionResult result)
{
    if (result is not Microsoft.AspNetCore.Mvc.ObjectResult { StatusCode: 409 } objectResult || objectResult.Value == null)
        return false;
    var message = objectResult.Value.GetType().GetProperty("message")?.GetValue(objectResult.Value)?.ToString();
    return message?.Contains("below the configured minimum", StringComparison.OrdinalIgnoreCase) == true;
}

static bool IsBadRequest(Microsoft.AspNetCore.Mvc.IActionResult result) =>
    result is Microsoft.AspNetCore.Mvc.BadRequestObjectResult;

static bool IsOk(Microsoft.AspNetCore.Mvc.IActionResult result) =>
    result is Microsoft.AspNetCore.Mvc.OkObjectResult;

static bool IsConflict(Microsoft.AspNetCore.Mvc.IActionResult result) =>
    result is Microsoft.AspNetCore.Mvc.ConflictObjectResult;

sealed class NoBroadcast : ILiveWeightBroadcaster
{
    public Task BroadcastAsync(LiveWeightDto dto) => Task.CompletedTask;
}

sealed class TestCurrentUser : ICurrentUser
{
    public int? UserId => 1;
    public string? Username => "test";
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
