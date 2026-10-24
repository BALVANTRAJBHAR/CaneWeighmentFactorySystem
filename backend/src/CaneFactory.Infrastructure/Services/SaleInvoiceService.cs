using CaneFactory.Application.Interfaces;
using CaneFactory.Application.Utilities;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Printing;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace CaneFactory.Infrastructure.Services;

/// <summary>
/// Single-sale tax invoice builder.  Excel is retained as the user-visible
/// editable/printable format, while the paired PDF makes silent printing to
/// the configured A4 Windows queue deterministic and Office-independent.
/// </summary>
public sealed class SaleInvoiceService(AppDbContext db) : ISaleInvoiceService
{
    private const string Green = "#B6D7A8";
    private const string LightGreen = "#E2F0D9";

    public async Task<byte[]> BuildExcelAsync(int salePurchaseId)
    {
        var data = await LoadAsync(salePurchaseId);
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Tax Invoice");
        ws.Style.Font.FontName = "Arial";
        ws.Style.Font.FontSize = 9;
        for (var column = 1; column <= 14; column++) ws.Column(column).Width = column switch
        {
            1 => 7, 2 => 15, 3 => 11, 4 => 12, 5 => 11, 6 => 9, 7 => 8,
            8 => 12, 9 => 8, 10 => 10, 11 => 10, 12 => 11, 13 => 11, _ => 13
        };

        var logoHeight = Math.Clamp(data.Company.ReportLogoHeight, 20, 200);
        var logoRowsHeight = Math.Max(98d, logoHeight * 0.75d + 8d);
        ws.Row(1).Height = logoRowsHeight * 0.39d;
        ws.Row(2).Height = logoRowsHeight * 0.39d;
        ws.Row(3).Height = logoRowsHeight * 0.22d;
        ws.Range("A1:N3").Merge();
        AddLogo(ws, data.Company.LogoPath, data.Company.LogoData, ws.Cell("A1"),
            Math.Clamp(data.Company.ReportLogoWidth, 40, 400),
            logoHeight);
        ws.Range("A4:N4").Merge().Value = $"Company Name: {data.Company.CompanyName}";
        ws.Range("A4:N4").Style.Font.Bold = true;
        ws.Range("A4:N10").Style.Fill.BackgroundColor = XLColor.FromHtml(LightGreen);
        ws.Range("A4:N10").Style.Alignment.WrapText = true;
        ws.Range("A4:N10").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        var companyLines = InvoiceCompanyLines(data.Company);
        for (var i = 0; i < companyLines.Count; i++)
        {
            ws.Range(5 + i, 1, 5 + i, 8).Merge().Value = companyLines[i];
        }
        ws.Range("I5:N5").Merge().Value = $"Phone No.: {ValueOrZero(data.Company.InvoicePhone)}";
        ws.Range("I6:N6").Merge().Value = $"Email ID: {ValueOrZero(data.Company.InvoiceEmail)}";
        ws.Range("I7:N7").Merge().Value = $"GSTIN No.: {ValueOrZero(data.Company.InvoiceGstin)}";
        ws.Range("I8:N8").Merge().Value = $"State: {ValueOrZero(data.Company.InvoiceState)}";

        ws.Range("A11:N11").Merge().Value = "TAX INVOICE";
        ws.Range("A11:N11").Style.Fill.BackgroundColor = XLColor.FromHtml(Green);
        ws.Range("A11:N11").Style.Font.Bold = true;
        ws.Range("A11:N11").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Range("A12:G12").Merge().Value = "Bill Details";
        ws.Range("H12:N12").Merge().Value = "Invoice Details";
        ws.Range("A12:N12").Style.Font.Bold = true;
        ws.Range("A12:N16").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("A12:N16").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        WriteMerged(ws, "A13:G13", $"Party Name: {data.Party.PartyName}");
        WriteMerged(ws, "A14:G14", $"Address: {ValueOrZero(data.Party.Address)}");
        WriteMerged(ws, "A15:D15", $"Phone No.: {data.Party.Mobile}");
        WriteMerged(ws, "E15:G15", $"Email ID: {ValueOrZero(data.Party.Email)}");
        WriteMerged(ws, "A16:G16", $"GSTIN No.: {ValueOrZero(data.Party.Gst)}");
        WriteMerged(ws, "H13:N13", $"Invoice No.: {data.Sale.Id}");
        WriteMerged(ws, "H14:N14", $"Invoice Date: {data.Date:dd-MM-yyyy}");
        WriteMerged(ws, "H15:N15", $"Time: {data.Date:HH:mm:ss}");
        WriteMerged(ws, "H16:K16", "Place of Supply: 0");
        WriteMerged(ws, "L16:N16", "PO Date: 0");
        WriteMerged(ws, "L17:N17", "PO Number: 0");

        var headers = new[] { "Sl. No.", "Item Name", "HSN/SAC", "Vehicle No", "Date", "MRP", "QTY", "Unit", "Price/Unit", "Disc", "GST Rate", "GST Amt", "Amount" };
        var headerColumns = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(18, i + 1).Value = headers[i];
            ws.Cell(18, i + 1).Style.Font.Bold = true;
            ws.Cell(18, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        ws.Range("A18:M18").Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAD3");
        ws.Range("A18:M18").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("A18:M18").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        var row = 19;
        var cells = new object[] { 1, data.Item.ItemName, ValueOrZero(data.Item.HsnSacCode), data.Sale.VehicleNumber,
            data.Date.ToString("dd-MMM-yy", CultureInfo.InvariantCulture), data.Rate, data.Quantity, "Qtl", data.Rate, 0m,
            data.Sale.GstPercent / 100m, data.GstAmount, data.FinalAmount };
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = ws.Cell(row, i + 1);
            switch (cells[i])
            {
                case int number: cell.Value = number; break;
                case decimal number: cell.Value = number; break;
                default: cell.Value = cells[i]?.ToString() ?? ""; break;
            }
        }
        ws.Range("A19:M19").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("A19:M19").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Range("F19:G19").Style.NumberFormat.Format = "0.00";
        ws.Range("I19:J19").Style.NumberFormat.Format = "0.00";
        ws.Range("K19:K19").Style.NumberFormat.Format = "0.00%";
        ws.Range("L19:M19").Style.NumberFormat.Format = "0.00";

        ws.Range("A21:I21").Merge().Value = "Description:";
        ws.Range("J21:L21").Merge().Value = "Sub Total";
        ws.Cell("M21").Value = data.BaseAmount;
        ws.Range("J22:L22").Merge().Value = "Discount";
        ws.Cell("M22").Value = 0m;
        ws.Range("J23:L23").Merge().Value = "Total Amount";
        ws.Cell("M23").Value = data.FinalAmount;
        ws.Range("J24:L24").Merge().Value = "Received";
        ws.Cell("M24").Value = 0m;
        ws.Range("J25:L25").Merge().Value = "Balance Amount";
        ws.Cell("M25").Value = 0m;
        ws.Range("A21:M25").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("A21:M25").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Range("J21:L25").Style.Font.Bold = true;
        ws.Range("M21:M25").Style.NumberFormat.Format = "0.00";

        ws.Range("A26:M27").Merge().Value = $"Invoice Amount In Words: {AmountInWords(data.FinalAmount)}";
        ws.Range("A26:M27").Style.Font.Bold = true;
        ws.Range("A26:M27").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("A29:H29").Merge().Value = "Terms and Conditions:";
        ws.Range("A29:H29").Style.Fill.BackgroundColor = XLColor.FromHtml(Green);
        ws.Range("A29:H29").Style.Font.Bold = true;
        ws.Range("A30:H35").Merge().Value = ValueOrZero(data.Company.InvoiceTermsAndConditions);
        ws.Range("A30:H35").Style.Alignment.WrapText = true;
        ws.Range("A29:H35").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("I29:M35").Merge().Value = $"For, {data.Company.CompanyName}\n\n\n\nCompany Seal & Signature";
        ws.Range("I29:M35").Style.Font.Bold = true;
        ws.Range("I29:M35").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("I29:M35").Style.Alignment.Vertical = XLAlignmentVerticalValues.Bottom;
        ws.Range("I29:M35").Style.Alignment.WrapText = true;
        ws.Range("I29:M35").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("A37:M37").Merge().Value = "POWERED BY WARRIOR SOFTECH";
        ws.Range("A37:M37").Style.Font.Bold = true;
        ws.Range("A37:M37").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.FitToPages(1, 1);
        ws.PageSetup.Margins.Top = 0.15; ws.PageSetup.Margins.Bottom = 0.15;
        ws.PageSetup.Margins.Left = 0.18; ws.PageSetup.Margins.Right = 0.18;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> BuildPdfAsync(int salePurchaseId)
    {
        var data = await LoadAsync(salePurchaseId);
        var logo = CompanyPrintHeader.ReadLogo(data.Company.LogoPath, data.Company.LogoData);
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(t => t.FontFamily("CaneLatin").FontSize(8));
                page.Content().Column(col =>
                {
                    if (logo != null)
                        col.Item().Width(Math.Clamp(data.Company.ReportLogoWidth, 40, 400))
                            .Height(Math.Clamp(data.Company.ReportLogoHeight, 20, 200))
                            .AlignCenter().Image(logo).FitArea();
                    col.Item().Background(Colors.Green.Lighten4).Padding(5).Column(c =>
                    {
                        c.Item().Text($"Company Name: {data.Company.CompanyName}").Bold();
                        foreach (var line in InvoiceCompanyLines(data.Company)) c.Item().Text(line);
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Text($"Phone No.: {ValueOrZero(data.Company.InvoicePhone)}");
                            r.RelativeItem().Text($"Email ID: {ValueOrZero(data.Company.InvoiceEmail)}");
                            r.RelativeItem().Text($"GSTIN No.: {ValueOrZero(data.Company.InvoiceGstin)}");
                        });
                    });
                    col.Item().Background(Colors.Green.Lighten3).AlignCenter().Padding(4).Text("TAX INVOICE").Bold().FontSize(11);
                    col.Item().PaddingTop(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                        table.Cell().Border(0.5f).Padding(3).Column(c =>
                        {
                            c.Item().Text("Bill Details").Bold();
                            c.Item().Text($"Party Name: {data.Party.PartyName}");
                            c.Item().Text($"Address: {ValueOrZero(data.Party.Address)}");
                            c.Item().Text($"Phone No.: {data.Party.Mobile}");
                            c.Item().Text($"Email ID: {ValueOrZero(data.Party.Email)}");
                            c.Item().Text($"GSTIN No.: {ValueOrZero(data.Party.Gst)}");
                        });
                        table.Cell().Border(0.5f).Padding(3).Column(c =>
                        {
                            c.Item().Text("Invoice Details").Bold();
                            c.Item().Text($"Invoice No.: {data.Sale.Id}");
                            c.Item().Text($"Invoice Date: {data.Date:dd-MM-yyyy}");
                            c.Item().Text($"Time: {data.Date:HH:mm:ss}");
                            c.Item().Text("Place of Supply: 0");
                            c.Item().Text("PO Date: 0");
                            c.Item().Text("PO Number: 0");
                        });
                    });
                    col.Item().PaddingTop(5).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(20); c.RelativeColumn(1.35f); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn();
                            c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(.7f); c.RelativeColumn(); c.RelativeColumn(.7f);
                            c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn();
                        });
                        var headers = new[] { "Sl.", "Item", "HSN/SAC", "Vehicle", "Date", "MRP", "QTY", "Unit", "Price/Unit", "Disc", "GST", "GST Amt", "Amount" };
                        table.Header(h => { foreach (var header in headers) h.Cell().Border(0.5f).Padding(2).AlignCenter().Text(header).Bold().FontSize(6.5f); });
                        var values = new[] { "1", data.Item.ItemName, ValueOrZero(data.Item.HsnSacCode), data.Sale.VehicleNumber,
                            data.Date.ToString("dd-MM-yy"), Money(data.Rate), data.Quantity.ToString("F2"), "Qtl", Money(data.Rate), "0.00",
                            $"{data.Sale.GstPercent:F2}%", Money(data.GstAmount), Money(data.FinalAmount) };
                        foreach (var value in values) table.Cell().Border(0.5f).Padding(2).Text(value).FontSize(6.5f);
                    });
                    col.Item().PaddingTop(5).Row(row =>
                    {
                        row.RelativeItem(1.4f).Column(c =>
                        {
                            c.Item().Text("Description:").Bold();
                            c.Item().PaddingTop(10).Text($"Invoice Amount In Words: {AmountInWords(data.FinalAmount)}").Bold();
                        });
                        row.RelativeItem().Table(table =>
                        {
                            var totals = new[] { ("Sub Total", data.BaseAmount), ("Discount", 0m), ("Total Amount", data.FinalAmount), ("Received", 0m), ("Balance Amount", 0m) };
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                            foreach (var (label, amount) in totals)
                            {
                                table.Cell().Border(0.5f).Padding(2).Text(label).Bold();
                                table.Cell().Border(0.5f).Padding(2).AlignRight().Text(Money(amount));
                            }
                        });
                    });
                    col.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().Border(0.5f).Padding(5).Column(c =>
                        {
                            c.Item().Text("Terms and Conditions:").Bold();
                            c.Item().Text(ValueOrZero(data.Company.InvoiceTermsAndConditions));
                        });
                        row.RelativeItem().Border(0.5f).Padding(5).AlignRight().AlignBottom()
                            .Text($"For, {data.Company.CompanyName}\n\n\nCompany Seal & Signature").Bold();
                    });
                    col.Item().PaddingTop(8).AlignCenter().Text("POWERED BY WARRIOR SOFTECH").Bold().FontSize(7);
                });
            });
        }).GeneratePdf();
    }

    private async Task<InvoiceData> LoadAsync(int salePurchaseId)
    {
        var sale = await db.SalePurchases.AsNoTracking().Include(x => x.Item).Include(x => x.Party)
            .FirstOrDefaultAsync(x => x.Id == salePurchaseId && !x.IsDeleted);
        if (sale == null) throw new KeyNotFoundException($"SalePurchase {salePurchaseId} not found.");
        if (sale.WeighmentStatus != "COMPLETED" || !sale.GrossDateTime.HasValue || !sale.FinalWeightQuintal.HasValue)
            throw new InvalidOperationException("Tax invoice is available only after the Sale Weighment final/gross save.");
        var company = await db.CompanyConfigs.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted)
            ?? throw new InvalidOperationException("Company configuration was not found.");
        var baseAmount = sale.Amount ?? 0m;
        var finalAmount = sale.FinalAmount ?? baseAmount + (sale.GstAmount ?? 0m);
        return new InvoiceData(sale, sale.Item, sale.Party, company, IndiaTime.ToIst(sale.GrossDateTime.Value),
            sale.Rate ?? 0m, sale.FinalWeightQuintal.Value, baseAmount, sale.GstAmount ?? 0m, finalAmount);
    }

    private static void AddLogo(IXLWorksheet ws, string? path, byte[]? storedLogo, IXLCell cell, int width, int height)
    {
        var bytes = CompanyPrintHeader.ReadLogo(path, storedLogo);
        if (bytes == null) return;
        using var stream = new MemoryStream(bytes);
        ws.AddPicture(stream).MoveTo(cell, 0, 0).WithSize(width, height);
    }

    private static void WriteMerged(IXLWorksheet ws, string range, string value)
    {
        ws.Range(range).Merge().Value = value;
    }

    private static List<string> InvoiceCompanyLines(CompanyConfig c) => new()
    {
        $"Road/Street: {ValueOrZero(c.InvoiceRoadStreet)}",
        $"Nearby Landmark: {ValueOrZero(c.InvoiceNearbyLandmark)}",
        $"Locality/Sub Locality: {ValueOrZero(c.InvoiceLocality)}",
        $"City/Town/Village: {ValueOrZero(c.InvoiceCity)}",
        $"District: {ValueOrZero(c.InvoiceDistrict)}",
        $"State: {ValueOrZero(c.InvoiceState)}   PIN Code: {ValueOrZero(c.InvoicePinCode)}"
    };

    private static string ValueOrZero(string? value) => string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string AmountInWords(decimal amount)
    {
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var rupees = (long)decimal.Truncate(rounded);
        var paise = (int)((rounded - rupees) * 100m);
        var text = NumberWords(rupees) + " Rupees";
        return paise == 0 ? text + " Only" : $"{text} and {NumberWords(paise)} Paise Only";
    }

    private static string NumberWords(long value)
    {
        if (value == 0) return "Zero";
        if (value < 0) return "Minus " + NumberWords(-value);
        var parts = new List<string>();
        foreach (var (amount, name) in new[] { (10000000L, "Crore"), (100000L, "Lakh"), (1000L, "Thousand"), (100L, "Hundred") })
        {
            if (value >= amount) { parts.Add(NumberWords(value / amount) + " " + name); value %= amount; }
        }
        if (value > 0) parts.Add(value < 20 ? new[] { "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" }[(int)value] :
            new[] { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" }[(int)(value / 10)] + (value % 10 == 0 ? "" : " " + NumberWords(value % 10)));
        return string.Join(" ", parts);
    }

    private sealed record InvoiceData(SalePurchase Sale, Item Item, Party Party, CompanyConfig Company, DateTime Date,
        decimal Rate, decimal Quantity, decimal BaseAmount, decimal GstAmount, decimal FinalAmount);
}
