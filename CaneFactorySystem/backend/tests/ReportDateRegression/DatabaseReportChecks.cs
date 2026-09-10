using CaneFactory.API.Controllers;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

internal static class DatabaseReportChecks
{
    // Opt-in, read-only: runs the real EF queries without starting API hosted
    // services, migrations, serial readers, printing, or sending messages.
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("CANE_CONNECTION_STRING")
            ?? throw new InvalidOperationException("CANE_CONNECTION_STRING is required.");
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        var export = new CountingExport();
        var controller = new ReportsController(db, new ReportReader(), export);
        foreach (var day in new[] { new DateTime(2026, 9, 7), new DateTime(2026, 9, 8) })
        foreach (var name in new[] { "Purchases", "Payments", "Loans", "SalePurchases", "DailyCollection" })
        {
            int? expected = null;
            foreach (var format in new[] { "json", "pdf", "excel" })
            {
                var method = typeof(ReportsController).GetMethod(name)!;
                var args = method.GetParameters().Select(p => p.Name switch
                {
                    "fromDate" or "toDate" => (object)day,
                    "format" => format,
                    _ => p.HasDefaultValue ? p.DefaultValue : null
                }).ToArray();
                var result = await (Task<IActionResult>)method.Invoke(controller, args)!;
                if (format == "json")
                {
                    if (result is not OkObjectResult ok) throw new Exception($"{name}: JSON failed");
                    var json = JsonSerializer.SerializeToElement(ok.Value);
                    expected = json.TryGetProperty("totalCount", out var count)
                        ? count.GetInt32() : json.GetProperty("rows").GetArrayLength();
                }
                else if (result is not FileContentResult || export.RowCount != Math.Min(expected!.Value, 5000))
                    throw new Exception($"{name}: {format} dataset does not match JSON");
            }
            Console.WriteLine($"PASS: {name} {day:dd-MM-yyyy}: {expected} rows; JSON/PDF/Excel query parity");
        }
    }

    private sealed class ReportReader : ICurrentUser
    {
        public int? UserId => null;
        public string? Username => "Read-only regression";
        public string? Role => "Developer";
        public string? Ip => null;
        public string? Device => null;
        public bool HasPermission(string code) => code.StartsWith("Report.") || code == "SalePurchase.View";
    }

    // Checks the dataset supplied to each exporter, not document rendering.
    private sealed class CountingExport : IReportExportService
    {
        public int RowCount { get; private set; }
        public byte[] ToPdf(string title, string? subtitle, List<string> headers, List<List<string>> rows, List<(string Label, string Value)>? totals = null)
        { RowCount = rows.Count; return []; }
        public byte[] ToExcel(string title, List<string> headers, List<List<string>> rows, List<(string Label, string Value)>? totals = null)
        { RowCount = rows.Count; return []; }
    }
}
