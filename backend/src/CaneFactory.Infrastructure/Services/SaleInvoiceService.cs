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
using System.Text;
using SkiaSharp;

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

    public async Task<byte[]> BuildExcelAsync(int salePurchaseId, bool isDuplicate = false)
    {
        var data = await LoadAsync(salePurchaseId);
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Tax Invoice");
        ws.Style.Font.FontName = "Arial";
        // A4 invoice print must remain readable after Excel applies the page fit.
        // Increase every invoice cell by one point and strengthen light body text.
        ws.Style.Font.FontSize = 10;
        ws.Style.Font.Bold = true;
        ws.Style.Font.FontColor = XLColor.Black;
        for (var column = 1; column <= 13; column++) ws.Column(column).Width = column switch
        {
            1 => 6, 2 => 16, 3 => 10, 4 => 14, 5 => 12, 6 => 10, 7 => 9,
            8 => 8, 9 => 12, 10 => 8, 11 => 10, 12 => 11, _ => 13
        };

        var logoHeight = Math.Clamp(data.Company.ReportLogoHeight, 20, 200);
        var logoRowsHeight = Math.Max(98d, logoHeight * 0.75d + 8d);
        ws.Row(1).Height = logoRowsHeight * 0.39d;
        ws.Row(2).Height = logoRowsHeight * 0.39d;
        ws.Row(3).Height = logoRowsHeight * 0.22d;
        ws.Range("A1:M3").Merge();
        AddLogo(ws, data.Company.LogoPath, data.Company.LogoData, ws.Cell("A1"),
            Enumerable.Range(1, 13).Sum(column => ws.Column(column).Width * 7d + 5d),
            logoRowsHeight * 0.95d);
        ws.Range("A4:M4").Merge().Value = $"Company Name: {data.Company.CompanyName}";
        ws.Range("A4:M4").Style.Font.Bold = true;
        ws.Range("A4:M10").Style.Fill.BackgroundColor = XLColor.FromHtml(LightGreen);
        ws.Range("A4:M10").Style.Alignment.WrapText = true;
        ws.Range("A4:M10").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        var companyLines = InvoiceCompanyLines(data.Company);
        for (var i = 0; i < companyLines.Count; i++)
        {
            ws.Range(5 + i, 1, 5 + i, 7).Merge().Value = companyLines[i];
        }
        ws.Range("H5:M5").Merge().Value = $"Phone No.: {ValueOrZero(data.Company.InvoicePhone)}";
        ws.Range("H6:M6").Merge().Value = $"Email ID: {ValueOrZero(data.Company.InvoiceEmail)}";
        ws.Range("H7:M7").Merge().Value = $"GSTIN No.: {ValueOrZero(data.Company.InvoiceGstin)}";
        ws.Range("H8:M8").Merge().Value = $"State: {ValueOrZero(data.Company.InvoiceState)}";

        ws.Range("A11:M11").Merge().Value = isDuplicate ? "DUPLICATE / REPRINT - TAX INVOICE" : "TAX INVOICE";
        ws.Range("A11:M11").Style.Fill.BackgroundColor = XLColor.FromHtml(Green);
        ws.Range("A11:M11").Style.Font.Bold = true;
        ws.Range("A11:M11").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Range("A12:F12").Merge().Value = "Bill Details";
        ws.Range("G12:M12").Merge().Value = "Invoice Details";
        ws.Range("A12:M12").Style.Font.Bold = true;
        ws.Range("A12:M17").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("A12:M17").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        WriteMerged(ws, "A13:F13", $"Party Name: {data.Party.PartyName}");
        WriteMerged(ws, "A14:F14", $"Address: {ValueOrZero(data.Party.Address)}");
        WriteMerged(ws, "A15:C15", $"Phone No.: {data.Party.Mobile}");
        WriteMerged(ws, "D15:F15", $"Email ID: {ValueOrZero(data.Party.Email)}");
        WriteMerged(ws, "A16:F16", $"GSTIN No.: {ValueOrZero(data.Party.Gst)}");
        WriteMerged(ws, "G13:M13", $"Invoice No.: {data.Sale.Id}");
        WriteMerged(ws, "G14:M14", $"Invoice Date: {data.Date:dd-MM-yyyy}");
        WriteMerged(ws, "G15:M15", $"Time: {data.Date:HH:mm:ss}");
        WriteMerged(ws, "G16:J16", $"Place of Supply: {SupplyLocality(data.Company)}");
        WriteMerged(ws, "K16:M16", "PO Date: 0");
        WriteMerged(ws, "K17:M17", "PO Number: 0");

        var headers = data.PrintAmountCalculation
            ? new[] { "Sl. No.", "Item Name", "HSN/SAC", "Vehicle No", "Date", "MRP", "QTY", "Unit", "Price/Unit", "Disc", "GST Rate", "GST Amt", "Amount" }
            : new[] { "Sl. No.", "Item Name", "Item Type", "HSN/SAC", "Vehicle No", "Date", "Item Quantity" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(18, i + 1).Value = headers[i];
            ws.Cell(18, i + 1).Style.Font.Bold = true;
            ws.Cell(18, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        var tableEndColumn = data.PrintAmountCalculation ? "M" : "G";
        ws.Range($"A18:{tableEndColumn}18").Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAD3");
        ws.Range($"A18:{tableEndColumn}18").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range($"A18:{tableEndColumn}18").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        var row = 19;
        var cells = data.PrintAmountCalculation
            ? new object[] { 1, data.Item.ItemName, ValueOrZero(data.Item.HsnSacCode), data.Sale.VehicleNumber,
                data.Date.ToString("dd-MMM-yy", CultureInfo.InvariantCulture), data.Rate, data.Quantity, "Qtl", data.Rate, 0m,
                data.Sale.GstPercent / 100m, data.GstAmount, data.FinalAmount }
            : new object[] { 1, data.Item.ItemName, ValueOrZero(data.Sale.ItemType), ValueOrZero(data.Item.HsnSacCode),
                data.Sale.VehicleNumber, data.Date.ToString("dd-MMM-yy", CultureInfo.InvariantCulture),
                data.Sale.ItemQuantity ?? 0m };
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
        ws.Range($"A19:{tableEndColumn}19").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range($"A19:{tableEndColumn}19").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        if (data.PrintAmountCalculation)
        {
            ws.Range("F19:G19").Style.NumberFormat.Format = "0.00";
            ws.Range("I19:J19").Style.NumberFormat.Format = "0.00";
            ws.Range("K19:K19").Style.NumberFormat.Format = "0.00%";
            ws.Range("L19:M19").Style.NumberFormat.Format = "0.00";
        }
        else ws.Cell("G19").Style.NumberFormat.Format = "0.###";

        ws.Range("A21:I25").Merge().Value = data.PrintAmountCalculation
            ? $"Description:\nItem Quantity: {QuantityText(data.Sale.ItemQuantity)} {ValueOrZero(data.Sale.ItemType)}\n" +
              $"Cutting: {data.Sale.CuttingPercent:0.##}% ({data.Sale.CuttingWeightQuintal:0.##} Qtl)\n" +
              $"Kanta Chhoot: {data.Sale.KantaChhootKgPerQuintal:0.##} kg/Qtl ({data.Sale.KantaChhootWeightQuintal:0.##} Qtl)"
            : $"Description:\nItem Quantity: {QuantityText(data.Sale.ItemQuantity)} {ValueOrZero(data.Sale.ItemType)}";
        ws.Range("A21:I25").Style.Alignment.WrapText = true;
        ws.Range("A21:I25").Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        ws.Range("A21:I25").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        if (data.PrintAmountCalculation)
        {
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
            ws.Range("J21:M25").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range("J21:M25").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Range("J21:L25").Style.Font.Bold = true;
            ws.Range("M21:M25").Style.NumberFormat.Format = "0.00";
            ws.Range("A26:M27").Merge().Value = $"Invoice Amount In Words: {AmountInWords(data.FinalAmount)}";
            ws.Range("A26:M27").Style.Font.Bold = true;
            ws.Range("A26:M27").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }
        ws.Range("A29:H29").Merge().Value = "Terms and Conditions:";
        ws.Range("A29:H29").Style.Fill.BackgroundColor = XLColor.FromHtml(Green);
        ws.Range("A29:H29").Style.Font.Bold = true;
        ws.Range("A30:H35").Merge().Value = ValueOrZero(data.Company.InvoiceTermsAndConditions);
        ws.Range("A30:H35").Style.Alignment.WrapText = true;
        ws.Range("A30:H35").Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        ws.Range("A29:H35").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("I29:M35").Merge().Value = $"For, {data.Company.CompanyName}\n\n\n\nCompany Seal & Signature";
        ws.Range("I29:M35").Style.Font.Bold = true;
        ws.Range("I29:M35").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("I29:M35").Style.Alignment.Vertical = XLAlignmentVerticalValues.Bottom;
        ws.Range("I29:M35").Style.Alignment.WrapText = true;
        ws.Range("I29:M35").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("A37:M37").Merge().Value = "WARRIOR SOFTECH";
        ws.Range("A37:M37").Style.Font.Bold = true;
        ws.Range("A37:M37").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.FitToPages(1, 1);
        ws.PageSetup.PrintAreas.Clear();
        ws.PageSetup.PrintAreas.Add("A1:M37");
        ws.PageSetup.CenterHorizontally = true;
        ws.PageSetup.CenterVertically = false;
        ws.PageSetup.Margins.Top = 0.15; ws.PageSetup.Margins.Bottom = 0.15;
        ws.PageSetup.Margins.Left = 0.18; ws.PageSetup.Margins.Right = 0.18;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> BuildPdfAsync(int salePurchaseId, bool isDuplicate = false)
    {
        var data = await LoadAsync(salePurchaseId);
        var logo = CompanyPrintHeader.ReadLogo(data.Company.LogoPath, data.Company.LogoData);
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(18);
                // Increase every PDF text style by one point. SemiBold keeps small
                // values legible on physical A4 prints without changing Dot Matrix.
                page.DefaultTextStyle(t => t.FontFamily("CaneLatin")
                    .FontSize(10).SemiBold().FontColor(Colors.Black));
                page.Content().AlignTop().Column(col =>
                {
                    if (logo != null)
                        // The configured invoice logo is a wide letterhead banner. FitWidth
                        // uses the complete printable width while preserving its aspect ratio.
                        col.Item().Image(logo).FitWidth();
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
                    col.Item().Background(Colors.Green.Lighten3).AlignCenter().Padding(4)
                        .Text(isDuplicate ? "DUPLICATE / REPRINT - TAX INVOICE" : "TAX INVOICE").Bold().FontSize(12);
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
                            c.Item().Text($"Place of Supply: {SupplyLocality(data.Company)}");
                            c.Item().Text("PO Date: 0");
                            c.Item().Text("PO Number: 0");
                        });
                    });
                    col.Item().PaddingTop(5).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(20); c.RelativeColumn(1.35f); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn();
                            c.RelativeColumn(); c.RelativeColumn();
                            if (data.PrintAmountCalculation)
                            {
                                c.RelativeColumn(.7f); c.RelativeColumn(); c.RelativeColumn(.7f);
                                c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn();
                            }
                        });
                        var headers = data.PrintAmountCalculation
                            ? new[] { "Sl.", "Item", "HSN/SAC", "Vehicle", "Date", "MRP", "QTY", "Unit", "Price/Unit", "Disc", "GST", "GST Amt", "Amount" }
                            : new[] { "Sl.", "Item", "Item Type", "HSN/SAC", "Vehicle", "Date", "Item Qty" };
                        table.Header(h => { foreach (var header in headers) h.Cell().Border(0.5f).Padding(2).AlignCenter().Text(header).Bold().FontSize(9); });
                        var values = data.PrintAmountCalculation
                            ? new[] { "1", data.Item.ItemName, ValueOrZero(data.Item.HsnSacCode), data.Sale.VehicleNumber,
                                data.Date.ToString("dd-MM-yy"), Money(data.Rate), data.Quantity.ToString("F2"), "Qtl", Money(data.Rate), "0.00",
                                $"{data.Sale.GstPercent:F2}%", Money(data.GstAmount), Money(data.FinalAmount) }
                            : new[] { "1", data.Item.ItemName, ValueOrZero(data.Sale.ItemType), ValueOrZero(data.Item.HsnSacCode),
                                data.Sale.VehicleNumber, data.Date.ToString("dd-MM-yy"), QuantityText(data.Sale.ItemQuantity) };
                        foreach (var value in values) table.Cell().Border(0.5f).Padding(2).Text(value).SemiBold().FontSize(9);
                    });
                    col.Item().PaddingTop(5).Row(row =>
                    {
                        row.RelativeItem(1.4f).Column(c =>
                        {
                            c.Item().Text("Description:").Bold();
                            c.Item().Text($"Item Quantity: {QuantityText(data.Sale.ItemQuantity)} {ValueOrZero(data.Sale.ItemType)}");
                            if (data.PrintAmountCalculation)
                            {
                                c.Item().Text($"Cutting: {data.Sale.CuttingPercent:0.##}% ({data.Sale.CuttingWeightQuintal:0.##} Qtl)");
                                c.Item().Text($"Kanta Chhoot: {data.Sale.KantaChhootKgPerQuintal:0.##} kg/Qtl ({data.Sale.KantaChhootWeightQuintal:0.##} Qtl)");
                                c.Item().PaddingTop(10).Text($"Invoice Amount In Words: {AmountInWords(data.FinalAmount)}").Bold();
                            }
                        });
                        if (data.PrintAmountCalculation) row.RelativeItem().Table(table =>
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
                    if (data.PrintAmountCalculation)
                        col.Item().PaddingTop(5).Row(row =>
                        {
                            row.RelativeItem().Border(0.5f).Padding(4).Column(c =>
                            {
                                c.Item().Text("Terms and Conditions / Bank Details:").Bold();
                                c.Item().Text(ValueOrZero(data.Company.InvoiceTermsAndConditions));
                            });
                            row.RelativeItem().Border(0.5f).Padding(4).AlignRight().AlignBottom()
                                .Text($"For, {data.Company.CompanyName}\n\nCompany Seal & Signature").Bold();
                        });
                    col.Item().PaddingTop(4).AlignCenter().Text("WARRIOR SOFTECH").Bold().FontSize(9);
                });
            });
        }).GeneratePdf();
    }

    /// <summary>
    /// Fast Sale Invoice for impact printers.  This intentionally contains no
    /// PNG/PDF/raster data: the queue receives resident-font ESC/P text, so an
    /// 80-column invoice is quick, cannot be clipped on the right and consumes
    /// exactly one logical continuous-paper page.
    /// </summary>
    public async Task<byte[]> BuildDotMatrixNativeAsync(int salePurchaseId, bool avoidCarriageReturn = false, bool isDuplicate = false)
    {
        var data = await LoadAsync(salePurchaseId);
        const int width = 80;
        const int pageLines = 66;
        var lines = new List<string>();
        void Add(string value = "") => lines.Add(Ascii(value, width));
        void Rule(char value = '-') => Add(new string(value, width));
        void Pair(string left, string right)
        {
            const int half = width / 2;
            lines.Add(Ascii(left, half - 1).PadRight(half) + Ascii(right, half));
        }
        void AddWrapped(string prefix, string? value)
        {
            var normalized = (prefix + (value ?? string.Empty)).Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var paragraph in normalized.Split('\n'))
            {
                var remaining = paragraph.Trim();
                while (remaining.Length > width)
                {
                    var split = remaining.LastIndexOf(' ', width - 1);
                    if (split <= 0) split = width;
                    Add(remaining[..split]);
                    remaining = remaining[split..].TrimStart();
                }
                Add(remaining);
            }
        }

        Add(Center(Ascii(data.Company.CompanyName, width), width));
        Add(Center(Ascii(string.Join(", ", InvoiceCompanyLines(data.Company)), width), width));
        Pair($"Phone: {ValueOrZero(data.Company.InvoicePhone)}", $"GSTIN: {ValueOrZero(data.Company.InvoiceGstin)}");
        Rule('=');
        Add(Center(isDuplicate ? "DUPLICATE / REPRINT - TAX INVOICE" : "TAX INVOICE", width));
        Rule('=');
        Pair($"Invoice No: {data.Sale.Id}", $"Date: {data.Date:dd-MM-yyyy} {data.Date:HH:mm:ss}");
        Pair($"Party: {data.Party.PartyName}", $"Mobile: {data.Party.Mobile}");
        Pair($"Address: {ValueOrZero(data.Party.Address)}", $"Party GSTIN: {ValueOrZero(data.Party.Gst)}");
        Pair($"Vehicle: {data.Sale.VehicleNumber}", $"HSN/SAC: {ValueOrZero(data.Item.HsnSacCode)}");
        Pair($"Place of Supply: {SupplyLocality(data.Company)}", "");
        Pair($"Item Qty: {QuantityText(data.Sale.ItemQuantity)}", $"Item Type: {ValueOrZero(data.Sale.ItemType)}");
        Rule();
        if (data.PrintAmountCalculation)
        {
            Add(Fixed("SL", 3) + Fixed("ITEM", 18) + Fixed("HSN/SAC", 10) + Fixed("VEHICLE", 12) +
                Fixed("QTY", 9, true) + Fixed("RATE", 11, true) + Fixed("AMOUNT", 17, true));
            Rule();
            Add(Fixed("1", 3) + Fixed(data.Item.ItemName, 18) + Fixed(ValueOrZero(data.Item.HsnSacCode), 10) +
                Fixed(data.Sale.VehicleNumber, 12) + Fixed(data.Quantity.ToString("F2"), 9, true) +
                Fixed(Money(data.Rate), 11, true) + Fixed(Money(data.BaseAmount), 17, true));
            Rule();
            Pair($"Cutting: {data.Sale.CuttingPercent:0.##}% / {data.Sale.CuttingWeightQuintal:0.##} Qtl",
                $"Kanta Chhoot: {data.Sale.KantaChhootKgPerQuintal:0.##} kg/Qtl / {data.Sale.KantaChhootWeightQuintal:0.##} Qtl");
            Pair($"GST Rate: {data.Sale.GstPercent:F2}%", $"Amount without GST: {Money(data.BaseAmount)}");
            Pair($"GST Amount: {Money(data.GstAmount)}", $"TOTAL: {Money(data.FinalAmount)}");
            Pair("Balance: 0.00", $"Final Weight: {data.Quantity:F2} Qtl");
            Rule();
            Add($"Amount in words: {AmountInWords(data.FinalAmount)}");
        }
        else
        {
            Add($"Item: {data.Item.ItemName}  HSN/SAC: {ValueOrZero(data.Item.HsnSacCode)}  Vehicle: {data.Sale.VehicleNumber}");
            Rule();
        }
        AddWrapped("Terms: ", ValueOrZero(data.Company.InvoiceTermsAndConditions));
        Add();
        Pair("Receiver Signature", $"For {data.Company.CompanyName}");
        Pair("", "Authorised Signatory");
        Add();
        Add(Center("WARRIOR SOFTECH", width));

        // Guarantee one and only one logical page.  Long user-entered values
        // are clipped per field, never wrapped into a second page.
        if (lines.Count > pageLines - 1) lines = lines.Take(pageLines - 1).ToList();
        while (lines.Count < pageLines - 1) lines.Add(string.Empty);

        using var stream = new MemoryStream();
        void Write(params byte[] bytes) => stream.Write(bytes, 0, bytes.Length);
        Write(0x1B, 0x40);             // ESC @: initialise
        Write(0x1B, 0x50);             // ESC P: 10 CPI resident font
        Write(0x1B, 0x32);             // ESC 2: 1/6-inch line spacing
        Write(0x1B, 0x43, (byte)pageLines);  // ESC C n: one 66-line form
        foreach (var line in lines)
        {
            var bytes = Encoding.ASCII.GetBytes(line);
            stream.Write(bytes, 0, bytes.Length);
            // Epson Auto LF can turn CR+LF into two paper advances.
            if (!avoidCarriageReturn) Write(0x0D);
            Write(0x0A);
        }
        Write(0x0C);                    // exactly one form feed/page
        return stream.ToArray();
    }

    private static string Fixed(string? value, int width, bool right = false)
    {
        var text = Ascii(value ?? string.Empty, width);
        return right ? text.PadLeft(width) : text.PadRight(width);
    }

    private static string Center(string value, int width)
    {
        var text = Ascii(value, width);
        var left = Math.Max(0, (width - text.Length) / 2);
        return new string(' ', left) + text;
    }

    private static string Ascii(string value, int width)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(ch is >= ' ' and <= '~' ? ch : '?');
            if (builder.Length == width) break;
        }
        return builder.ToString();
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
        var printAmountCalculation = await db.PrintConfigs.AsNoTracking().Where(x => !x.IsDeleted)
            .Select(x => (bool?)x.SalePurchaseAmountCalculationPrintEnabled).FirstOrDefaultAsync() ?? true;
        var baseAmount = sale.Amount ?? 0m;
        var finalAmount = sale.FinalAmount ?? baseAmount + (sale.GstAmount ?? 0m);
        return new InvoiceData(sale, sale.Item, sale.Party, company, IndiaTime.ToIst(sale.GrossDateTime.Value),
            sale.Rate ?? 0m, sale.FinalWeightQuintal.Value, baseAmount, sale.GstAmount ?? 0m, finalAmount,
            printAmountCalculation);
    }

    private static void AddLogo(IXLWorksheet ws, string? path, byte[]? storedLogo, IXLCell cell,
        double maximumWidth, double maximumHeight)
    {
        var bytes = CompanyPrintHeader.ReadLogo(path, storedLogo);
        if (bytes == null) return;
        using var codecStream = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(codecStream);
        if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0) return;
        var scale = Math.Min(maximumWidth / codec.Info.Width, maximumHeight / codec.Info.Height);
        var width = Math.Max(1, (int)Math.Floor(codec.Info.Width * scale));
        var height = Math.Max(1, (int)Math.Floor(codec.Info.Height * scale));
        using var stream = new MemoryStream(bytes);
        ws.AddPicture(stream).MoveTo(cell, (int)((maximumWidth - width) / 2), 0).WithSize(width, height);
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
    private static string SupplyLocality(CompanyConfig company) =>
        !string.IsNullOrWhiteSpace(company.InvoiceLocality) ? company.InvoiceLocality.Trim() :
        !string.IsNullOrWhiteSpace(company.InvoiceCity) ? company.InvoiceCity.Trim() : "-";
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string QuantityText(decimal? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-";

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
        decimal Rate, decimal Quantity, decimal BaseAmount, decimal GstAmount, decimal FinalAmount,
        bool PrintAmountCalculation);
}
