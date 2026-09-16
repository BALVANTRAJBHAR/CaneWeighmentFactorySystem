using CaneFactory.API.Controllers;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

internal static class ExportBrandingChecks
{
    public static async Task RunAsync()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        foreach (var font in new[] { "Tinos-Regular.ttf", "Tinos-Bold.ttf" })
        {
            using var stream = File.OpenRead(Path.Combine("backend/src/CaneFactory.API/Assets/Fonts", font));
            FontManager.RegisterFontWithCustomName("CaneLatin", stream);
        }
        var connection = Environment.GetEnvironmentVariable("CANE_CONNECTION_STRING")!;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        var company = await db.CompanyConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        var controller = new ReportsController(db, new Reader(), new ReportExportService(db));
        var output = Path.Combine(Path.GetTempPath(), "CaneReportBrandingSamples");
        Directory.CreateDirectory(output);
        foreach (var name in new[] { "Purchases", "Payments", "SalePurchases", "Loans", "DailyCollection" })
        foreach (var format in new[] { "pdf", "excel" })
        {
            var method = typeof(ReportsController).GetMethod(name)!;
            var args = method.GetParameters().Select(p => p.Name switch
            {
                "fromDate" => (object)new DateTime(2026, 9, 1),
                "toDate" => new DateTime(2026, 9, 9),
                "format" => format,
                _ => p.HasDefaultValue ? p.DefaultValue : null
            }).ToArray();
            var result = await (Task<IActionResult>)method.Invoke(controller, args)!;
            if (result is not FileContentResult file || file.FileContents.Length < 100) throw new Exception($"{name} export failed");
            if (format == "excel")
            {
                using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
                var sheet = workbook.Worksheet(1);
                if (sheet.Cell(1, 2).GetString() != (company?.CompanyName ?? "Cane Factory")) throw new Exception("Company header mismatch");
                var expectedPictures = !string.IsNullOrWhiteSpace(company?.LogoPath) && File.Exists(company.LogoPath) ? 2 : 1;
                if (sheet.Pictures.Count != expectedPictures) throw new Exception("Logo/QR missing");
                foreach (var header in sheet.Row(7).CellsUsed().Where(c => c.GetString().Contains("Date")))
                {
                    var cell = sheet.Cell(8, header.Address.ColumnNumber);
                    if (cell.IsEmpty() || cell.GetString() == "-") continue;
                    if (cell.DataType != XLDataType.DateTime || cell.Style.DateFormat.Format != "dd-mm-yyyy") throw new Exception("Date is not typed/formatted correctly");
                }
            }
            await File.WriteAllBytesAsync(Path.Combine(output, name + (format == "pdf" ? ".pdf" : ".xlsx")), file.FileContents);
            Console.WriteLine($"PASS: {name} {format} rendered with configured branding");
        }
        Console.WriteLine($"Samples: {output}");
    }

    private sealed class Reader : ICurrentUser
    {
        public int? UserId => null;
        public string? Username => "Report verification";
        public string? Role => "Developer";
        public string? Ip => null;
        public string? Device => null;
        public bool HasPermission(string code) => code.StartsWith("Report.") || code == "SalePurchase.View";
    }
}
