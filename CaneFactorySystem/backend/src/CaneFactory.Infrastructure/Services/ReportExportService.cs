using CaneFactory.Application.Interfaces;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CaneFactory.Infrastructure.Services;

/// <summary>Phase 11: shared generic tabular renderer for every report endpoint (Purchases, Payments,
/// Loans, Daily Collection, Audit). Landscape A4 for PDF (reports are wide); ClosedXML for Excel.</summary>
public class ReportExportService : IReportExportService
{
    public byte[] ToPdf(string title, string? subtitle, List<string> headers, List<List<string>> rows,
        List<(string Label, string Value)>? totals = null)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontFamily("CaneLatin").FontSize(8));

                page.Header().Column(col =>
                {
                    col.Item().Text(title).FontSize(15).Bold().FontColor(Colors.Blue.Darken2);
                    if (!string.IsNullOrWhiteSpace(subtitle))
                        col.Item().Text(subtitle).FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Blue.Darken2);
                });

                page.Content().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        foreach (var _ in headers) c.RelativeColumn();
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
                    });
                });
            });
        });
        return document.GeneratePdf();
    }

    public byte[] ToExcel(string title, List<string> headers, List<List<string>> rows,
        List<(string Label, string Value)>? totals = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Report");

        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, Math.Max(headers.Count, 1)).Merge();

        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(3, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1565C0");
            cell.Style.Font.FontColor = XLColor.White;
        }

        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Count; c++)
                ws.Cell(4 + r, c + 1).Value = rows[r][c];

        var totalsStartRow = 4 + rows.Count + 1;
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
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }
}
