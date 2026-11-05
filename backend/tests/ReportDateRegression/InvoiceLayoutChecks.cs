using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Services;
using CaneFactory.Infrastructure.Printing;
using CaneFactory.Domain.Entities;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;
using SkiaSharp;
using System.Text;

internal static class InvoiceLayoutChecks
{
    public static async Task RunAsync()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        foreach (var font in new[] { "Tinos-Regular.ttf", "Tinos-Bold.ttf" })
        {
            using var stream = File.OpenRead(Path.Combine("backend/src/CaneFactory.API/Assets/Fonts", font));
            FontManager.RegisterFontWithCustomName("CaneLatin", stream);
        }
        foreach (var font in new[] { "NotoSansDevanagari-Regular.ttf", "NotoSansDevanagari-Bold.ttf" })
        {
            using var stream = File.OpenRead(Path.Combine("backend/src/CaneFactory.API/Assets/Fonts", font));
            FontManager.RegisterFontWithCustomName("CaneDevanagari", stream);
        }
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        using var bitmap = new SKBitmap(1200, 120);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.DarkGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var encodedLogo = image.Encode(SKEncodedImageFormat.Png, 100);
        var company = new CompanyConfig { CompanyName = "Test Sugar Mill", InvoiceLocality = "Kodsa",
            InvoiceCity = "Kareli", LogoData = encodedLogo.ToArray(), ReportLogoHeight = 100,
            InvoiceTermsAndConditions = "Account Holder: Test Sugar Mill\nBank Name: Test Bank\nA/C: 123456\nIFSC: TEST0001\nBranch: Kodsa" };
        var item = new Item { ItemName = "Jaggery", HsnSacCode = "17011410" };
        var party = new Party { PartyName = "Test Trader", Mobile = "9876543210" };
        var vehicleType = new VehicleType { VehicleTypeName = "Truck", VehicleTypeNameHi = "ट्रक" };
        var operatorUser = new User { Username = "operator", FullName = "Deepa", FullNameHi = "दीपा" };
        var printConfig = new PrintConfig { Language = "hi", PrintImages = false,
            SalePurchaseAmountCalculationPrintEnabled = true };
        db.AddRange(company, item, party, vehicleType, operatorUser, printConfig);
        var sale = new SalePurchase { Item = item, Party = party, VehicleType = vehicleType,
            VehicleNumber = "MP20AB1234", DriverName = "Driver", TareByUserName = "operator",
            GrossByUserName = "operator",
            TareDateTime = DateTime.UtcNow.AddHours(-1), GrossDateTime = DateTime.UtcNow,
            TareWeightQuintal = 10m, GrossWeightQuintal = 60m, FinalWeightQuintal = 50m,
            Rate = 400m, ItemQuantity = 125m, ItemType = "Jaggery Box", CuttingPercent = 2m,
            CuttingWeightQuintal = 1m, KantaChhootKgPerQuintal = 1m, KantaChhootWeightQuintal = 0.5m,
            Amount = 20000m, GstPercent = 5m, GstAmount = 1000m,
            FinalAmount = 21000m, WeighmentStatus = "COMPLETED" };
        db.SalePurchases.Add(sale);
        await db.SaveChangesAsync();
        var saleId = sale.Id;
        var printEngine = new PrintEngineService(db, Array.Empty<CaneFactory.Application.Interfaces.IPrintRenderer>(), null!);
        var tareSlip = await printEngine.BuildSalePurchaseSlipAsync(saleId, "TARE", "operator", false);
        var grossSlip = await printEngine.BuildSalePurchaseSlipAsync(saleId, "GROSS", "operator", false);
        if (tareSlip.TitleHindi != "बिक्री तौल पर्ची - टेयर" ||
            grossSlip.TitleHindi != "बिक्री तौल पर्ची - अंतिम" ||
            tareSlip.Rows.Any(x => x.LabelEnglish == "Rate") ||
            tareSlip.Rows.Single(x => x.LabelEnglish == "Tare Operator").Value != "दीपा" ||
            grossSlip.Rows.Single(x => x.LabelEnglish == "Gross Operator").Value != "दीपा" ||
            grossSlip.Rows.Single(x => x.LabelEnglish == "Item Quantity (Bags/Boxes/Units)").Value != "125" ||
            grossSlip.Rows.Single(x => x.LabelEnglish == "Item Type").Value != "Jaggery Box" ||
            grossSlip.Rows.Single(x => x.LabelEnglish == "Cutting").Value.StartsWith("2%") == false ||
            grossSlip.Rows.Single(x => x.LabelEnglish == "Kanta Chhoot").Value.StartsWith("1 kg/Qtl") == false ||
            grossSlip.Rows.Single(x => x.LabelEnglish == "Vehicle Type").Value != "ट्रक" ||
            grossSlip.Rows.Single(x => x.LabelEnglish == "SalePurchase ID").LabelHindi != "बिक्री/आरएसटी क्रमांक")
            throw new Exception("Sale slip Hindi labels, operator names or tare rate are incorrect.");
        if (grossSlip.A4Rows == null ||
            grossSlip.A4Rows.Any(x => x.LabelEnglish is "Tare Operator" or "Vehicle Number" or "Item Type" or "GST Amount (Rs)") ||
            grossSlip.A4Rows.Single(x => x.LabelEnglish == "Vehicle").Value != "ट्रक / MP20AB1234" ||
            grossSlip.A4Rows.Single(x => x.LabelEnglish == "Item (Quantity / Type)").Value != "125 Jaggery Box" ||
            grossSlip.A4Rows.Single(x => x.LabelEnglish == "GST").Value != "5% / 1000.00")
            throw new Exception("Sale A4 compact vehicle, item or GST rows are incorrect.");
        grossSlip.PrintImages = true;
        grossSlip.Images = new()
        {
            new CaneFactory.Application.DTOs.PrintImage("Gross Camera", "", encodedLogo.ToArray()),
            new CaneFactory.Application.DTOs.PrintImage("Tare Camera", "", encodedLogo.ToArray())
        };
        var standardPdf = new A4PdfRenderer().RenderFinal(grossSlip).bytes;
        var zone = new Zone { Id = 1, ZoneName = "Test Zone" };
        var village = new Village { Id = 101, Zone = zone, VillageName = "Test Village" };
        var grower = new Grower { Id = 1000, Village = village, GrowerName = "Test Grower" };
        var varietyType = new VarietyType { VarietyTypeName = "Plant" };
        var variety = new Variety { VarietyType = varietyType, VarietyName = "Test Variety" };
        var season = new Season { SeasonName = "2026-27", StartDate = DateTime.UtcNow.Date };
        var purchase = new Purchase { Grower = grower, VillageId = village.Id,
            VehicleType = vehicleType, VehicleNumber = "MP20AB1234", VarietyType = varietyType,
            Variety = variety, Season = season, GrossByUserName = "operator", TareByUserName = "operator",
            GrossDateTime = DateTime.UtcNow.AddHours(-1), TareDateTime = DateTime.UtcNow,
            GrossWeightQuintal = 60m, TareWeightQuintal = 10m, Rate = 400m };
        db.Purchases.Add(purchase);
        await db.SaveChangesAsync();
        var purchaseGrossSlip = await printEngine.BuildGrossSlipAsync(purchase.Id, "operator", false);
        var purchaseSlip = await printEngine.BuildTareSlipAsync(purchase.Id, "operator", false);
        if (purchaseGrossSlip.Rows.Any(x => x.LabelEnglish.Contains("Rate", StringComparison.OrdinalIgnoreCase)) ||
            purchaseSlip.Rows.Single(x => x.LabelEnglish == "Gross Operator").Value != "दीपा" ||
            purchaseSlip.Rows.Single(x => x.LabelEnglish == "Tare Operator").Value != "दीपा" ||
            purchaseSlip.Rows.Single(x => x.LabelEnglish == "Vehicle").Value.StartsWith("ट्रक") == false)
            throw new Exception("Sugarcane slip Hindi operator or vehicle names are incorrect.");
        var service = new SaleInvoiceService(db);
        var excel = await service.BuildExcelAsync(saleId);
        using var workbook = new XLWorkbook(new MemoryStream(excel));
        var sheet = workbook.Worksheet(1);
        if (!sheet.MergedRanges.Any(x => x.RangeAddress.ToString() == "A1:M3") ||
            sheet.Cell("N4").IsEmpty() == false ||
            sheet.Style.Font.FontSize != 10 ||
            !sheet.Cell("A13").Style.Font.Bold ||
            !sheet.Cell("G16").GetString().StartsWith("Place of Supply: ") ||
            sheet.Cell("G16").GetString() != "Place of Supply: Kodsa" ||
            !sheet.Cell("A21").GetString().Contains("125") ||
            !sheet.Cell("A30").GetString().Contains("IFSC: TEST0001") ||
            sheet.Cell("A11").GetString() != "TAX INVOICE" ||
            sheet.Cell("A37").GetString() != "WARRIOR SOFTECH" ||
            sheet.PageSetup.PageOrientation != XLPageOrientation.Landscape ||
            sheet.Pictures.Count != 1)
            throw new Exception("Invoice Excel header, locality, footer or print layout is incorrect.");
        var duplicateExcel = await service.BuildExcelAsync(saleId, true);
        using var duplicateWorkbook = new XLWorkbook(new MemoryStream(duplicateExcel));
        if (!duplicateWorkbook.Worksheet(1).Cell("A11").GetString().Contains("DUPLICATE / REPRINT"))
            throw new Exception("Invoice duplicate marking is missing.");
        var pdf = await service.BuildPdfAsync(saleId);
        if (pdf.Length < 1000) throw new Exception("Invoice PDF is empty.");
        var dotMatrix = Encoding.ASCII.GetString(await service.BuildDotMatrixNativeAsync(saleId));
        if (!dotMatrix.Contains("Item Qty: 125") ||
            !dotMatrix.Contains("Item Type: Jaggery Box") ||
            !dotMatrix.Contains("Cutting: 2%") ||
            !dotMatrix.Contains("Kanta Chhoot: 1 kg/Qtl") ||
            !dotMatrix.Contains("IFSC: TEST0001"))
            throw new Exception("Invoice dot-matrix quantity or multiline bank terms are missing.");
        printConfig.SalePurchaseAmountCalculationPrintEnabled = false;
        await db.SaveChangesAsync();
        var hiddenSlip = await printEngine.BuildSalePurchaseSlipAsync(saleId, "GROSS", "operator", false);
        var prohibited = new[] { "Cutting", "Kanta Chhoot", "Final Weight (Qtl)", "Rate", "Amount Without GST (Rs)", "GST Rate (%)", "GST Amount (Rs)", "Final Amount (Rs)" };
        if (prohibited.Any(label => hiddenSlip.Rows.Any(x => x.LabelEnglish == label)) ||
            !hiddenSlip.Rows.Any(x => x.LabelEnglish == "Item Type"))
            throw new Exception("Sale amount-calculation print switch did not hide all configured fields.");
        var hiddenExcel = await service.BuildExcelAsync(saleId);
        using var hiddenWorkbook = new XLWorkbook(new MemoryStream(hiddenExcel));
        var hiddenText = string.Join("|", hiddenWorkbook.Worksheet(1).CellsUsed().Select(x => x.GetString()));
        if (new[] { "GST Rate", "GST Amt", "Final Amount", "Cutting", "Kanta Chhoot" }.Any(hiddenText.Contains) ||
            !hiddenText.Contains("Jaggery Box"))
            throw new Exception("Sale invoice Excel amount-calculation switch is incorrect.");
        var hiddenDotMatrix = Encoding.ASCII.GetString(await service.BuildDotMatrixNativeAsync(saleId));
        if (new[] { "CUTTING", "KANTA CHHOOT", "GST AMT", "FINAL AMOUNT" }.Any(hiddenDotMatrix.Contains) ||
            !hiddenDotMatrix.Contains("Item Type: Jaggery Box"))
            throw new Exception("Sale invoice dot-matrix amount-calculation switch is incorrect.");
        var output = Path.Combine(Path.GetTempPath(), "CaneInvoiceLayoutSamples");
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, "sale-invoice.xlsx"), excel);
        await File.WriteAllBytesAsync(Path.Combine(output, "sale-invoice.pdf"), pdf);
        await File.WriteAllBytesAsync(Path.Combine(output, "sale-standard-with-images.pdf"), standardPdf);
        Console.WriteLine($"PASS: sale invoice #{saleId} Excel/PDF layout generated at {output}");
    }
}
