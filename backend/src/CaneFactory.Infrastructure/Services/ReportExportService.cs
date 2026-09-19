using CaneFactory.Application.Interfaces;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using CaneFactory.Infrastructure.Persistence;
using CaneFactory.Infrastructure.Printing;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CaneFactory.Infrastructure.Services;

/// <summary>Phase 11: shared generic tabular renderer for every report endpoint (Purchases, Payments,
/// Loans, Daily Collection, Audit). Landscape A4 for PDF (reports are wide); ClosedXML for Excel.</summary>
public class ReportExportService : IReportExportService
{
    private readonly AppDbContext _db;
    public ReportExportService(AppDbContext db) => _db = db;

    private (string Name, string? Address, byte[]? Logo, byte[] Qr) Branding(string title, List<string> headers, List<List<string>> rows)
    {
        var company = _db.CompanyConfigs.AsNoTracking().FirstOrDefault(c => !c.IsDeleted);
        // A list report has many transaction IDs: identify the complete dataset,
        // not an arbitrary first purchase. No credentials or personal data in QR.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { title, headers, rows }))));
        return (company?.CompanyName ?? "Cane Factory", company?.Address,
            CompanyPrintHeader.ReadLogo(company?.LogoPath), QrCodeHelper.GeneratePng($"Report: {title}\nSHA256: {hash}", 4));
    }

    private static float ColumnWeight(string header) => header switch
    {
        "Grower Name" or "Party" or "Item" => 1.45f,
        "Vehicle Number" or "Vehicle No" or "Variety" => 1.2f,
        "Purchase Date (Tare)" or "Cutting Weight (Qtl)" => 1.15f,
        "Weighment Status" => 1.25f,
        _ => 1f
    };

    public byte[] ToPdf(string title, string? subtitle, List<string> headers, List<List<string>> rows,
        List<(string Label, string Value)>? totals = null)
    {
        var branding = Branding(title, headers, rows);
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontFamily("CaneLatin").FontSize(8));

                page.Header().Column(col =>
                {
                    col.Item().Element(e => CompanyPrintHeader.Compose(e, branding.Name, branding.Address, branding.Logo, branding.Qr));
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Blue.Darken2);
                    col.Item().Text(title).FontSize(15).Bold().FontColor(Colors.Blue.Darken2);
                    if (!string.IsNullOrWhiteSpace(subtitle))
                        col.Item().Text(subtitle).FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Blue.Darken2);
                });

                page.Content().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        foreach (var header in headers) c.RelativeColumn(ColumnWeight(header));
                    });
                    table.Header(h =>
                    {
                        foreach (var head in headers)
                            h.Cell().Background(Colors.Blue.Darken2).Padding(4)
                                .Text(head).FontSize(8).Bold().FontColor(Colors.White);
                    });
                    foreach (var row in rows)
                        foreach (var cell in row)
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                .Text(cell ?? "").FontSize(7.5f);
                });

                page.Footer().Column(col =>
                {
                    if (totals is { Count: > 0 })
                    {
                        col.Item().PaddingTop(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                        col.Item().PaddingTop(4).Row(row =>
                        {
                            foreach (var (label, value) in totals)
                                row.RelativeItem().Text($"{label}: {value}").FontSize(8).Bold();
                        });
                    }
                    col.Item().PaddingTop(6).AlignCenter().Text(t =>
                    {
                        t.Span("Rows: ").FontSize(7);
                        t.Span(rows.Count.ToString()).FontSize(7).Bold();
                        t.Span($"   Generated: {DateTime.UtcNow:dd-MM-yyyy HH:mm} UTC").FontSize(7);
                        t.Span("   Page ");
                        t.CurrentPageNumber().FontSize(7);
                        t.Span(" of ");
                        t.TotalPages().FontSize(7);
                        t.Span("   •   Warrior Softech").Bold().FontSize(7);
                    });
                });
            });
        });
        return document.GeneratePdf();
    }

    public byte[] ToExcel(string title, List<string> headers, List<List<string>> rows,
        List<(string Label, string Value)>? totals = null)
    {
        var branding = Branding(title, headers, rows);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Report");
        ws.Style.Font.FontName = "Arial";
        ws.Style.Font.FontSize = 10;
        ws.Rows().Height = 20;
        var lastColumn = Math.Max(headers.Count, 4);
        ws.Range(1, 2, 1, lastColumn - 1).Merge().Value = branding.Name;
        ws.Range(1, 2, 1, lastColumn - 1).Style.Font.SetBold().Font.SetFontSize(16);
        ws.Range(1, 2, 2, lastColumn - 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(1, 2, 2, lastColumn - 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Range(1, 2, 1, lastColumn - 1).Style.Alignment.WrapText = true;
        ws.Range(2, 2, 2, lastColumn - 1).Merge().Value = branding.Address ?? "";
        ws.Range(2, 2, 2, lastColumn - 1).Style.Alignment.WrapText = true;
        ws.Row(1).Height = 60;
        ws.Row(2).Height = 45;
        if (branding.Logo != null)
        {
            using var logo = new MemoryStream(branding.Logo);
            var picture = ws.AddPicture(logo).MoveTo(ws.Cell(1, 1), 4, 4);
            picture.Scale(Math.Min(70d / picture.Width, 70d / picture.Height));
        }
        using (var qr = new MemoryStream(branding.Qr))
            ws.AddPicture(qr).MoveTo(ws.Cell(1, lastColumn), 4, 4).WithSize(76, 76);
        ws.Range(4, 1, 4, lastColumn).Merge().Value = title;
        ws.Cell(4, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Range(5, 1, 5, lastColumn).Merge().Value = $"Generated: {DateTime.UtcNow:dd-MM-yyyy HH:mm} UTC";
        const int headerRow = 7;

        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1565C0");
            cell.Style.Font.FontColor = XLColor.White;
        }

        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Count; c++)
            {
                var cell = ws.Cell(headerRow + 1 + r, c + 1);
                var value = rows[r][c];
                if (headers[c].Contains("Date", StringComparison.OrdinalIgnoreCase) &&
                    DateTime.TryParseExact(value, new[] { "dd-MM-yyyy", "dd-MM-yyyy HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    cell.Value = date.Date;
                    cell.Style.DateFormat.Format = "dd-mm-yyyy";
                }
                else cell.Value = value;
            }

        var totalsStartRow = headerRow + rows.Count + 2;
        if (totals is { Count: > 0 })
        {
            for (var i = 0; i < totals.Count; i++)
            {
                ws.Cell(totalsStartRow + i, 1).Value = totals[i].Label;
                ws.Cell(totalsStartRow + i, 1).Style.Font.Bold = true;
                ws.Cell(totalsStartRow + i, 2).Value = totals[i].Value;
            }
        }

        ws.Columns().AdjustToContents();
        foreach (var column in ws.ColumnsUsed()) column.Width = Math.Clamp(column.Width, 14, 32);
        if (headers.Count <= 5) ws.Columns(1, lastColumn).Width = 32;
        ws.Range(headerRow, 1, headerRow, lastColumn).Style.Alignment.WrapText = true;
        ws.Row(headerRow).Height = 32;
        ws.SheetView.FreezeRows(headerRow);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(1, headerRow);
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }
}
