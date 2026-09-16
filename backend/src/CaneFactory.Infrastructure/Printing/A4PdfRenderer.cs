using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CaneFactory.Infrastructure.Printing;

/// <summary>A4 (laser/inkjet) renderer via QuestPDF. Preview == Final (the PDF is already WYSIWYG).</summary>
public class A4PdfRenderer : IPrintRenderer
{
    public string TargetType => "A4";

    public (byte[] bytes, string contentType, string fileExtension) RenderFinal(PrintDocument doc) =>
        (Build(doc), "application/pdf", "pdf");

    public (byte[] bytes, string contentType, string fileExtension) RenderPreview(PrintDocument doc) => RenderFinal(doc);

    private static byte[] Build(PrintDocument doc)
    {
        var fontFamily = string.Equals(doc.Language, "hi", StringComparison.OrdinalIgnoreCase) ? "CaneDevanagari" : "CaneLatin";
        var hindi = string.Equals(doc.Language, "hi", StringComparison.OrdinalIgnoreCase);
        byte[]? qr = doc.QrValue.HasValue ? QrCodeHelper.GeneratePng(doc.QrValue.Value.ToString(), 8) : null;
        byte[]? logo = !string.IsNullOrWhiteSpace(doc.LogoPath) && File.Exists(doc.LogoPath) ? File.ReadAllBytes(doc.LogoPath) : null;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontFamily(fontFamily).FontSize(10).FontColor(Colors.Black));

                page.Header().Column(col =>
                {
                    col.Item().Element(e => CompanyPrintHeader.Compose(e, doc.CompanyName, doc.Address, logo, qr));
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Blue.Darken2);
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text(hindi ? doc.TitleHindi : doc.TitleEnglish)
                            .FontSize(13).Bold().FontColor(Colors.Blue.Darken2);
                        if (!string.IsNullOrWhiteSpace(doc.SeasonName))
                            row.ConstantItem(160).AlignRight().Text($"{(doc.Language == "hi" ? "पेराई सत्र" : "Season")}: {doc.SeasonName}").FontSize(9).FontColor(Colors.Blue.Darken1);
                    });
                });

                page.Content().PaddingTop(10).Column(content =>
                {
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1.1f); c.RelativeColumn(1.4f);
                            c.RelativeColumn(1.1f); c.RelativeColumn(1.4f);
                        });
                        for (var i = 0; i < doc.Rows.Count; i += 2)
                        {
                            var r1 = doc.Rows[i];
                            var r2 = i + 1 < doc.Rows.Count ? doc.Rows[i + 1] : null;
                            AddCell(table, hindi ? r1.LabelHindi : r1.LabelEnglish, true);
                            AddCell(table, r1.Value, false);
                            AddCell(table, r2 != null ? (hindi ? r2.LabelHindi : r2.LabelEnglish) : "", true);
                            AddCell(table, r2?.Value ?? "", false);
                        }
                    });

                    var printableImages = doc.PrintImages ? doc.Images
                        .Where(image => !string.IsNullOrWhiteSpace(image.FilePath) && File.Exists(image.FilePath))
                        .ToList() : new List<PrintImage>();
                    if (printableImages.Count > 0)
                    {
                        // Keep a clear blank line between the detail grid and the
                        // evidence section so printed photos never look attached
                        // to the final detail row.
                        content.Item().PaddingTop(18).Text(hindi ? "तौल चित्र" : "Weighment Images")
                            .FontSize(10).Bold().FontColor(Colors.Blue.Darken2);
                        content.Item().PaddingTop(3).Table(imagesTable =>
                        {
                            imagesTable.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            foreach (var image in printableImages)
                            {
                                imagesTable.Cell().Padding(4).Column(imageCell =>
                                {
                                    imageCell.Item().AlignCenter().Text(image.Label).FontSize(8)
                                        .FontColor(Colors.Grey.Darken1);
                                    // 174pt is 20% larger than the previous 145pt evidence
                                    // frame while retaining two photos per A4 row.
                                    imageCell.Item().PaddingTop(2).Height(174).AlignCenter()
                                        .Image(File.ReadAllBytes(image.FilePath)).FitArea();
                                });
                            }

                            if (printableImages.Count % 2 != 0)
                                imagesTable.Cell().Padding(4).Text(string.Empty);
                        });
                    }
                });

                page.Footer().PaddingTop(8).Column(col =>
                {
                    col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text($"{(doc.Language == "hi" ? "द्वारा जनरेट किया गया" : "Generated By")}: {doc.GeneratedByUserName}").FontSize(8).FontColor(Colors.Grey.Darken1);
                        row.RelativeItem().AlignRight().Text($"Print: {doc.PrintDateTime:dd-MM-yyyy HH:mm:ss}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                    col.Item().AlignCenter().PaddingTop(2).Text(doc.Language == "hi" ? "कंप्यूटर जनरेटेड पर्ची" : "This is a computer-generated slip.").FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });
        });
        return document.GeneratePdf();
    }

    private static void AddCell(TableDescriptor table, string text, bool isLabel)
    {
        var cell = table.Cell().Padding(3).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
        if (isLabel) cell.Text(text).FontSize(9).Bold().FontColor(Colors.Blue.Darken2);
        else cell.Text(text).FontSize(9).FontColor(Colors.Black);
    }
}
