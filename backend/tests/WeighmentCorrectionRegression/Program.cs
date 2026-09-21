using System.Text.Json;
using CaneFactory.API.Controllers;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

// Isolated in-memory database only. No production database, serial device, SMS or printing.
int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
JsonElement Json(IActionResult result) => JsonSerializer.SerializeToElement(((ObjectResult)result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
int Status(IActionResult result) => ((ObjectResult)result).StatusCode ?? 200;
async Task<WeighmentCorrectionRequest> Request(Fixture f, int id = 1, string kind = "purchase") =>
    new(2, " up32 AB-1234 ", Json(await f.Controller.Get(kind, id)).GetProperty("record").GetProperty("revision").GetString()!,
        kind == "purchase" ? 2 : null, kind == "purchase" ? 2 : null, 325.50m);

var auth = typeof(WeighmentCorrectionsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
Check(auth.Roles == "Admin,Developer", "HTTP authorization limits roles");
await using (var f = await Fixture.Create())
{
    foreach (var role in new[] { "Operator", "SubAdmin", "Accountant", "Farmer", "" })
    {
        var user = new Actor(role);
        var c = new WeighmentCorrectionsController(f.Db, user, new AuditService(f.Db, user));
        Check(Status(await c.Get("purchase", 1)) == 403, role + " read denied despite edit permission");
        Check(Status(await c.Update("purchase", 1, new(1, "UP32AB1234", "x"))) == 403, role + " update denied");
    }
    var request = await Request(f);
    var preview = await f.Controller.Preview("purchase", 1, request);
    Check(Status(preview) == 200 && Json(preview).GetProperty("amount").GetDecimal() == 3336.38m, "server preview rounds final weight times effective rate");
    Check(await f.Db.AuditLogs.CountAsync() == 0, "preview does not audit or write");
    Check((await f.Db.Purchases.AsNoTracking().SingleAsync(x => x.Id == 1)).Rate == 300m, "preview leaves original price intact");
    var result = await f.Controller.Update("purchase", 1, request);
    Check(Status(result) == 200, "unpaid correction succeeds");
    var saved = await f.Db.Purchases.AsNoTracking().SingleAsync(x => x.Id == 1);
    Check(saved.VarietyTypeId == 2 && saved.VarietyId == 2 && saved.VehicleTypeId == 2 && saved.VehicleNumber == "UP32AB1234", "all editable fields saved and vehicle normalized");
    Check(saved.Rate == 325.50m && saved.PurchaseAmount == 3336.38m, "amount repriced");
    Check(saved.GrossWeightQuintal == 25.25m && saved.TareWeightQuintal == 15m && saved.FinalWeightQuintal == 10.25m, "weights untouched");
    Check(saved.UpdatedBy == 7 && saved.UpdatedAt.HasValue && saved.GrossByUserName == "original", "updated attribution preserves original operator");
    var audit = await f.Db.AuditLogs.SingleAsync();
    Check(audit.Username == "admin-test" && audit.OldValue!.Contains("300") && audit.NewValue!.Contains("3336.38"), "atomic before-after audit with username");
    Check(Status(await f.Controller.Update("purchase", 1, request)) == 409, "stale form cannot overwrite correction");
    var reload = Json(await f.Controller.Get("purchase", 1));
    Check(reload.GetProperty("record").GetProperty("revision").GetString() == Json(result).GetProperty("revision").GetString(), "revision stable after database UTC roundtrip");
    var vehicleOnly = (await Request(f)) with { VehicleNumber = "UP32CD2222" };
    await f.Db.Rates.Where(x => x.Id == 2).ExecuteUpdateAsync(s => s.SetProperty(x => x.Rate, 900m));
    Check(Status(await f.Controller.Update("purchase", 1, vehicleOnly)) == 200, "vehicle-only edit does not reprice from new master rate");
    Check((await f.Db.Purchases.AsNoTracking().SingleAsync(x => x.Id == 1)).Rate == 325.50m, "original type rate snapshot preserved");
}

foreach (var paidBy in new[] { "flag", "status", "link" })
{
    await using var f = await Fixture.Create();
    var request = await Request(f);
    if (paidBy == "flag") await f.Db.Purchases.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.PaymentFlag, "Y"));
    if (paidBy == "status") await f.Db.Purchases.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.PaymentStatus, "PAID"));
    if (paidBy == "link")
    {
        f.Db.Payments.Add(new Payment { Id = 101, AdviceNumber = 101, GrowerId = 1, PaymentModeId = 1, SeasonId = 1 });
        f.Db.PaymentPurchases.Add(new PaymentPurchase { PaymentId = 101, PurchaseId = 1, PurchaseAmountAtPayment = 3075 });
        await f.Db.SaveChangesAsync();
    }
    foreach (var response in new[] { await f.Controller.Get("purchase", 1), await f.Controller.Preview("purchase", 1, request), await f.Controller.Update("purchase", 1, request) })
        Check(Status(response) == 409 && Json(response).GetProperty("code").GetString() == "PAYMENT_COMPLETED", "paid " + paidBy + " blocks lookup, preview and update");
    Check(await f.Db.AuditLogs.CountAsync() == 0 && (await f.Db.Purchases.AsNoTracking().SingleAsync(x => x.Id == 1)).Rate == 300m, "paid rejection never mutates record");
}

await using (var f = await Fixture.Create())
{
    var req = await Request(f);
    Check(Status(await f.Controller.Update("purchase", 1, req with { VarietyId = 1 })) == 400, "mismatched variety rejected");
    Check(Status(await f.Controller.Update("purchase", 1, req with { ExpectedRate = 999 })) == 409, "changed/unpreviewed rate rejected");
    Check(Status(await f.Controller.Update("purchase", 1, req with { VehicleTypeId = 999 })) == 400, "missing vehicle type rejected");
    Check(Status(await f.Controller.Update("purchase", 1, req with { VehicleNumber = "?" })) == 400, "invalid vehicle rejected");
    await f.Db.Rates.Where(x => x.Id == 2).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, false));
    Check(Status(await f.Controller.Update("purchase", 1, req)) == 409, "missing applicable rate rejected (future rate not used)");
    await f.Db.Purchases.Where(x => x.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.LockStatus, "LOCKED"));
    Check(Status(await f.Controller.Update("purchase", 1, req)) == 409, "locked purchase rejected");
}

await using (var f = await Fixture.Create())
{
    await f.Db.Purchases.Where(x => x.Id == 1 || x.Id == 2).ExecuteUpdateAsync(s => s
        .SetProperty(x => x.GrossTareStatus, "GROSS_DONE").SetProperty(x => x.TareWeightQuintal, (decimal?)null)
        .SetProperty(x => x.FinalWeightQuintal, (decimal?)null).SetProperty(x => x.PurchaseAmount, (decimal?)null));
    var req = (await Request(f)) with { VehicleNumber = "up32 cc 9999" };
    Check(Status(await f.Controller.Update("purchase", 1, req)) == 409, "normalized duplicate pending cane rejected");
    var result = await f.Controller.Update("purchase", 1, req with { VehicleNumber = "UP32ZZ9999" });
    Check(Status(result) == 200 && Json(result).GetProperty("amount").ValueKind == JsonValueKind.Null, "pending cane may change type but amount waits for final weight");
}

await using (var f = await Fixture.Create())
{
    var user = new Actor("Developer");
    var c = new WeighmentCorrectionsController(f.Db, user, new AuditService(f.Db, user));
    var req = await Request(f, kind: "sale");
    Check(Status(await c.Update("sale", 1, req)) == 200, "Developer can correct completed sale vehicle");
    var sale = await f.Db.SalePurchases.AsNoTracking().SingleAsync();
    Check(sale.VehicleNumber == "UP32AB1234" && sale.VehicleTypeId == 2 && sale.Rate == 400 && sale.Amount == 4000 && sale.FinalWeightQuintal == 10, "sale item, party, rate and weights preserved");
    req = await Request(f, kind: "sale");
    Check(Status(await c.Update("sale", 1, req with { VarietyTypeId = 1 })) == 400, "sale rejects irrelevant cane variety fields");
}

// Two independent contexts: a payment loaded before a correction must fail its stale UPDATE.
await using (var f = await Fixture.Create())
{
    await using var payer = f.NewContext();
    var stale = await payer.Purchases.SingleAsync(x => x.Id == 1);
    Check(Status(await f.Controller.Update("purchase", 1, await Request(f))) == 200, "concurrent correction committed");
    stale.PaymentStatus = "PAID"; stale.PaymentFlag = "Y"; stale.UpdatedAt = DateTime.UtcNow;
    bool blocked = false;
    try { await payer.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { blocked = true; }
    Check(blocked, "stale payment cannot save original price after correction");
}

// Exercise all real payment controller paths and their full rollback on a stale purchase.
foreach (var mode in new[] { "SINGLE", "FARMER", "DATE_RANGE" })
{
    await using var f = await Fixture.Create();
    var controller = new PaymentController(f.Db, new AuditService(f.Db, f.User), f.User,
        new InterleavingSequence(f.Db), new MemoryCache(new MemoryCacheOptions()), new NoSms());
    bool blocked = false;
    try
    {
        await controller.Issue(new PaymentCreateRequest { SelectionMode = mode, PurchaseId = mode == "SINGLE" ? 1 : null,
            GrowerCode = mode == "DATE_RANGE" ? "" : "101/1", PaymentModeId = 1,
            FromDate = new DateTime(2026, 9, 1), ToDate = new DateTime(2026, 9, 30) });
    }
    catch (DbUpdateConcurrencyException) { blocked = true; }
    f.Db.ChangeTracker.Clear();
    Check(blocked, mode + " stale payment detected");
    Check(!await f.Db.Payments.AnyAsync() && !await f.Db.PaymentPurchases.AnyAsync() && !await f.Db.CashBookEntries.AnyAsync(), mode + " payment, links and cash rolled back");
    Check(!await f.Db.NumberSequences.AnyAsync() && !await f.Db.LoanRecoveries.AnyAsync(), mode + " sequence and loan side effects rolled back");
    Check((await f.Db.Loans.AsNoTracking().SingleAsync()).OutstandingAmount == 100, mode + " loan balance unchanged after rollback");
}

// Normal payments must still work, and must use the corrected price in every selection mode.
foreach (var mode in new[] { "SINGLE", "FARMER", "DATE_RANGE" })
{
    await using var f = await Fixture.Create();
    if (mode == "DATE_RANGE")
    {
        f.Db.Growers.Add(new Grower { Id = 2, VillageId = 101, GrowerSequence = 2, GrowerCode = "101/2", GrowerName = "Second farmer" });
        await f.Db.SaveChangesAsync();
        await f.Db.Purchases.Where(x => x.Id == 2).ExecuteUpdateAsync(s => s.SetProperty(x => x.GrowerId, 2).SetProperty(x => x.GrowerCode, "101/2"));
    }
    Check(Status(await f.Controller.Update("purchase", 1, await Request(f))) == 200, mode + " correction before payment");
    var controller = new PaymentController(f.Db, new AuditService(f.Db, f.User), f.User,
        new SequenceGenerator(f.Db), new MemoryCache(new MemoryCacheOptions()), new NoSms());
    var response = await controller.Issue(new PaymentCreateRequest { SelectionMode = mode,
        PurchaseId = mode == "SINGLE" ? 1 : null, GrowerCode = mode == "DATE_RANGE" ? "" : "101/1", PaymentModeId = 1,
        FromDate = new DateTime(2026, 9, 1), ToDate = new DateTime(2026, 9, 30) });
    Check(Status(response) == 200, mode + " normal payment succeeds");
    var payments = await f.Db.Payments.AsNoTracking().ToListAsync();
    var total = mode == "SINGLE" ? 3336.38m : 6411.38m;
    Check(payments.Sum(x => x.TotalPurchaseAmount) == total && payments.Sum(x => x.NetPayableAmount) == total - 100m,
        mode + " payment uses corrected amount and loan recovery");
    Check(payments.Count == (mode == "DATE_RANGE" ? 2 : 1), mode + " expected number of grower payments");
    Check((await f.Db.PaymentPurchases.AsNoTracking().SingleAsync(x => x.PurchaseId == 1)).PurchaseAmountAtPayment == 3336.38m,
        mode + " immutable payment snapshot uses corrected price");
    Check(Status(await f.Controller.Get("purchase", 1)) == 409, mode + " paid record now cannot be corrected");
}

await using (var f = await Fixture.Create())
{
    var c = new WeighmentCorrectionsController(f.Db, f.User, new FailingAudit(f.Db, f.User));
    bool failed = false;
    try { await c.Update("purchase", 1, await Request(f)); } catch (InvalidOperationException) { failed = true; }
    f.Db.ChangeTracker.Clear();
    Check(failed && !await f.Db.AuditLogs.AnyAsync() && (await f.Db.Purchases.SingleAsync(x => x.Id == 1)).Rate == 300, "audit failure rolls back the entire correction");
}
Console.WriteLine($"PASS: {checks} weighment correction checks (in-memory SQLite, no production writes).");

sealed class Actor(string role = "Admin") : ICurrentUser
{
    public int? UserId => 7;
    public string? Username => "admin-test";
    public string? Role => role;
    public string? Ip => null;
    public string? Device => "regression";
    public bool HasPermission(string code) => true;
}

sealed class Fixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public Actor User { get; } = new();
    public AppDbContext Db { get; private set; } = null!;
    public WeighmentCorrectionsController Controller => new(Db, User, new AuditService(Db, User));
    public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
    public static async Task<Fixture> Create()
    {
        var f = new Fixture();
        await f._connection.OpenAsync(); f.Db = f.NewContext();
        await f.Db.Database.EnsureCreatedAsync();
        f.Db.AddRange(new User { Id = 7, Username = "admin-test" }, new Zone { Id = 1, ZoneName = "Test" },
            new Village { Id = 101, ZoneId = 1, VillageName = "Test" }, new Grower { Id = 1, VillageId = 101, GrowerCode = "101/1", GrowerName = "Test farmer" },
            new VehicleType { Id = 1, VehicleTypeName = "Tractor" }, new VehicleType { Id = 2, VehicleTypeName = "Truck" },
            new VarietyType { Id = 1, VarietyTypeName = "Normal" }, new VarietyType { Id = 2, VarietyTypeName = "Early" },
            new Variety { Id = 1, VarietyTypeId = 1, VarietyName = "N" }, new Variety { Id = 2, VarietyTypeId = 2, VarietyName = "E" },
            new RateMaster { Id = 2, VarietyTypeId = 2, Rate = 325.50m, EffectiveFrom = new DateTime(2026, 9, 1), EffectiveTo = new DateTime(2026, 9, 30) },
            new RateMaster { Id = 3, VarietyTypeId = 2, Rate = 900m, EffectiveFrom = new DateTime(2026, 10, 1) },
            new Season { Id = 1, SeasonName = "2026", IsActive = true },
            new Item { Id = 1, ItemName = "Test item" }, new Party { Id = 1, PartyName = "Test party" },
            new PaymentModeMaster { Id = 1, ModeCode = "CASH", ModeName = "Cash" },
            new LoanTypeMaster { Id = 1, LoanTypeName = "Advance" },
            new Loan { Id = 1, GrowerId = 1, GrowerCode = "101/1", VillageId = 101, LoanTypeId = 1,
                LoanAmount = 100, OutstandingAmount = 100, SeasonId = 1, IssueDate = new DateTime(2026, 9, 1) });
        for (var id = 1; id <= 2; id++)
            f.Db.Purchases.Add(new Purchase { Id = id, GrowerId = 1, GrowerCode = "101/1", VillageId = 101,
                VehicleTypeId = 1, VehicleNumber = id == 1 ? "UP32AA1111" : "UP32CC9999", VarietyTypeId = 1, VarietyId = 1,
                GrossWeightQuintal = 25.25m, TareWeightQuintal = 15m, FinalWeightQuintal = 10.25m, Rate = 300, PurchaseAmount = 3075,
                GrossDateTime = new DateTime(2026, 9, 10), TareDateTime = new DateTime(2026, 9, 11), GrossByUserName = "original",
                GrossTareStatus = "TARE_DONE", PaymentStatus = "PENDING", SeasonId = 1 });
        f.Db.SalePurchases.Add(new SalePurchase { Id = 1, ItemId = 1, PartyId = 1, VehicleTypeId = 1, VehicleNumber = "UP32SS1234",
            TareWeightQuintal = 5, GrossWeightQuintal = 15, FinalWeightQuintal = 10, Rate = 400, Amount = 4000, WeighmentStatus = "COMPLETED" });
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear(); return f;
    }
    public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
}

sealed class InterleavingSequence(AppDbContext db) : ISequenceGenerator
{
    private bool _once;
    public async Task<long> NextAsync(string name, long startValue = 1)
    {
        // Emulate the changed DB version after the payment's selection was materialized.
        if (!_once) { _once = true; await db.Database.ExecuteSqlRawAsync("UPDATE Purchases SET UpdatedAt = '2026-09-20 23:59:59' WHERE Id = 1"); }
        return await new SequenceGenerator(db).NextAsync(name, startValue);
    }
}
sealed class FailingAudit(AppDbContext db, ICurrentUser user) : IAuditService
{
    public async Task LogAsync(string action, string module, string? entity = null, string? entityId = null, object? oldValue = null,
        object? newValue = null, bool success = true, string? failureReason = null)
    {
        await new AuditService(db, user).LogAsync(action, module, entity, entityId, oldValue, newValue, success, failureReason);
        throw new InvalidOperationException("Simulated failure after audit flush");
    }
}
sealed class NoSms : ISmsService
{
    public Task<bool> QueueAsync(string e, int g, string m, string r, Dictionary<string,string> p) => Task.FromResult(false);
    public Task<bool> QueueForPartyAsync(string e, int g, string m, string r, Dictionary<string,string> p) => Task.FromResult(false);
    public Task<bool> QueueForOperationalRecipientAsync(string e, string m, string r, Dictionary<string,string> p) => Task.FromResult(false);
    public Task<bool> QueueRawAsync(string e, int? g, int? party, string m, string r, string message) => Task.FromResult(false);
}
