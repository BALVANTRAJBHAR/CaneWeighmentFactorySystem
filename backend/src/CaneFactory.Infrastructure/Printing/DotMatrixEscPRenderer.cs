using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace CaneFactory.Infrastructure.Printing;

/// <summary>
/// TVS MSP 270 Classic Plus (9-pin, ESC/P2-compatible) renderer. The slip is first rasterized to a
/// monochrome SKBitmap using proper HarfBuzz text shaping (so Hindi conjuncts/matras render
/// correctly - the printer's own font is never used), then converted 1:1 to Epson ESC * bit-image
/// graphics. RenderPreview returns that exact source bitmap as PNG, so preview == print (WYSIWYG).
/// </summary>
public class DotMatrixEscPRenderer : IPrintRenderer
{
    public string TargetType => "DotMatrix";

    private const int WidthPx = 960;        // 8" usable width @ 120 dpi (ESC * m=1, double density)
    private const byte LineSpacingUnits = 20; // ESC '3' n -> n/180" ; one 8-dot band = 8/72" = 20/180"

    public (byte[] bytes, string contentType, string fileExtension) RenderPreview(PrintDocument doc)
    {
        using var bmp = Draw(doc);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return (data.ToArray(), "image/png", "png");
    }

    public (byte[] bytes, string contentType, string fileExtension) RenderFinal(PrintDocument doc)
    {
        using var bmp = Draw(doc);
        return (ToEscP(bmp, doc.UseHalfPageDotMatrixLayout), "application/octet-stream", "prn");
    }

    private static SKBitmap Draw(PrintDocument doc) =>
        doc.UseHalfPageDotMatrixLayout ? DrawHalfPage(doc) : DrawLegacy(doc);

    /// <summary>Fixed-height production layout for cane gross/final and sale tare/final.
    /// One render advances exactly half of the configured continuous page, so two consecutive
    /// weighment jobs occupy the two pre-printed sections without state or an intermediate FF.</summary>
    private static SKBitmap DrawHalfPage(PrintDocument doc)
    {
        var hindi = string.Equals(doc.Language, "hi", StringComparison.OrdinalIgnoreCase);
        var regular = PrintFonts.Get(doc.Language, bold: false);
        var bold = PrintFonts.Get(doc.Language, bold: true);
        var rows = ExpandRows(doc);

        var pageLines = Math.Clamp(doc.DotMatrixPageLines, 80, 180);
        if (pageLines % 2 != 0) pageLines--;
        var halfPageLines = pageLines / 2;
        var maxHeaderLines = Math.Max(4, halfPageLines - 34);
        var headerLines = Math.Clamp(doc.DotMatrixHeaderReservedLines, 4, maxHeaderLines);
        var height = halfPageLines * 8;
        var headerHeight = headerLines * 8;

        var bmp = new SKBitmap(WidthPx, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        using var titleFont = new SKFont(bold, 18);
        using var labelFont = new SKFont(bold, 15);
        using var valueFont = new SKFont(regular, 15);
        using var shaperBold = new SKShaper(bold);
        using var shaperRegular = new SKShaper(regular);

        if (doc.DotMatrixPrintHeader)
            DrawReservedHeader(canvas, doc, regular, bold, paint, headerHeight);

        // No separator is drawn beneath the company header. This keeps the QR quiet zone clear.
        var y = headerHeight + 2;
        var title = hindi ? doc.TitleHindi : doc.TitleEnglish;
        DrawFittedText(canvas, title, shaperBold, bold, 18, 12,
            new SKRect(10, y, WidthPx - 280, y + 24), paint, SKTextAlign.Left);
        if (!string.IsNullOrWhiteSpace(doc.SeasonName))
        {
            var season = $"{(hindi ? "पेराई सत्र" : "Season")}: {doc.SeasonName}";
            DrawFittedText(canvas, season, shaperRegular, regular, 15, 10,
                new SKRect(WidthPx - 270, y, WidthPx - 10, y + 24), paint, SKTextAlign.Right);
        }
        y += 27;
        canvas.DrawLine(10, y, WidthPx - 10, y, paint);
        y += 5;

        if (doc.IsDuplicate)
        {
            DrawFittedText(canvas, "*** DUPLICATE / REPRINT ***", shaperBold, bold, 16, 11,
                new SKRect(10, y, WidthPx - 10, y + 22), paint, SKTextAlign.Center);
            y += 24;
        }

        const int footerHeight = 44;
        var footerY = height - footerHeight;
        var pairRows = Math.Max(1, (int)Math.Ceiling(rows.Count / 2.0));
        var rowHeight = Math.Min(22, Math.Max(16, (footerY - y - 2) / pairRows));
        var colWidth = WidthPx / 2;
        for (var i = 0; i < rows.Count; i += 2)
        {
            DrawCompactRow(canvas, rows[i], hindi, shaperBold, shaperRegular, bold, regular,
                labelFont, valueFont, paint, 10, y, colWidth, rowHeight);
            if (i + 1 < rows.Count)
                DrawCompactRow(canvas, rows[i + 1], hindi, shaperBold, shaperRegular, bold, regular,
                    labelFont, valueFont, paint, colWidth + 10, y, colWidth, rowHeight);
            y += rowHeight;
        }

        canvas.DrawLine(10, footerY, WidthPx - 10, footerY, paint);
        DrawFittedText(canvas,
            $"{(hindi ? "द्वारा जनरेट किया गया" : "Generated By")}: {doc.GeneratedByUserName}",
            shaperRegular, regular, 14, 9, new SKRect(10, footerY + 3, WidthPx / 2, footerY + 23),
            paint, SKTextAlign.Left);
        DrawFittedText(canvas, $"Print: {doc.PrintDateTime:dd-MM-yyyy HH:mm:ss}",
            shaperRegular, regular, 14, 9, new SKRect(WidthPx / 2, footerY + 3, WidthPx - 10, footerY + 23),
            paint, SKTextAlign.Right);
        DrawFittedText(canvas, "Warrior Softech", shaperBold, bold, 13, 9,
            new SKRect(10, footerY + 22, WidthPx - 10, height - 2), paint, SKTextAlign.Center);

        canvas.Flush();
        return bmp;
    }

    private static void DrawReservedHeader(SKCanvas canvas, PrintDocument doc, SKTypeface regular,
        SKTypeface bold, SKPaint paint, int headerHeight)
    {
        const int sideBox = 82;
        if (!string.IsNullOrWhiteSpace(doc.LogoPath) && File.Exists(doc.LogoPath))
        {
            try
            {
                using var logo = SKBitmap.Decode(doc.LogoPath);
                if (logo != null)
                {
                    var maxSide = Math.Max(24, Math.Min(58, headerHeight - 12));
                    var scale = Math.Min((float)maxSide / logo.Width, (float)maxSide / logo.Height);
                    var w = logo.Width * scale;
                    var h = logo.Height * scale;
                    canvas.DrawBitmap(logo, new SKRect(10 + (sideBox - w) / 2, (headerHeight - h) / 2,
                        10 + (sideBox + w) / 2, (headerHeight + h) / 2),
                        new SKSamplingOptions(SKFilterMode.Linear));
                }
            }
            catch { }
        }

        using var shaperBold = new SKShaper(bold);
        using var shaperRegular = new SKShaper(regular);
        var textLeft = 10 + sideBox;
        var textRight = WidthPx - sideBox - 10;
        var companyBottom = Math.Min(headerHeight - 24, 36);
        DrawFittedText(canvas, doc.CompanyName, shaperBold, bold, 24, 11,
            new SKRect(textLeft, 2, textRight, Math.Max(22, companyBottom)), paint, SKTextAlign.Center);
        if (!string.IsNullOrWhiteSpace(doc.Address))
            DrawFittedText(canvas, doc.Address, shaperRegular, regular, 14, 8,
                new SKRect(textLeft, Math.Max(24, companyBottom), textRight, headerHeight - 5), paint, SKTextAlign.Center);

        if (doc.QrValue.HasValue)
        {
            var qrPng = QrCodeHelper.GeneratePng(doc.QrValue.Value.ToString(), 3);
            using var qrBmp = SKBitmap.Decode(qrPng);
            if (qrBmp != null)
            {
                var side = Math.Max(28, Math.Min(64, headerHeight - 12));
                var left = WidthPx - side - 14;
                var top = (headerHeight - side) / 2f;
                // Explicit white quiet-zone backing prevents any nearby raster content merging.
                canvas.DrawRect(new SKRect(left - 4, top - 4, left + side + 4, top + side + 4),
                    new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill });
                canvas.DrawBitmap(qrBmp, new SKRect(left, top, left + side, top + side),
                    new SKSamplingOptions(SKFilterMode.Nearest));
            }
        }
    }

    private static void DrawCompactRow(SKCanvas canvas, PrintRow row, bool hindi, SKShaper shaperBold,
        SKShaper shaperRegular, SKTypeface bold, SKTypeface regular, SKFont labelFont, SKFont valueFont,
        SKPaint paint, int x, int y, int columnWidth, int rowHeight)
    {
        var baselineTop = y + Math.Max(0, (rowHeight - 19) / 2);
        var label = (hindi ? row.LabelHindi : row.LabelEnglish) + ":";
        DrawFittedText(canvas, label, shaperBold, bold, labelFont.Size, 9,
            new SKRect(x, baselineTop, x + 220, baselineTop + 19), paint, SKTextAlign.Left);
        DrawFittedText(canvas, row.Value, shaperRegular, regular, valueFont.Size, 8,
            new SKRect(x + 225, baselineTop, x + columnWidth - 12, baselineTop + 19), paint, SKTextAlign.Left);
    }

    private static void DrawFittedText(SKCanvas canvas, string text, SKShaper shaper, SKTypeface typeface,
        float preferredSize, float minimumSize, SKRect bounds, SKPaint paint, SKTextAlign align)
    {
        var size = preferredSize;
        using var font = new SKFont(typeface, size);
        while (size > minimumSize && shaper.Shape(text, font).Width > bounds.Width)
        {
            size -= 0.5f;
            font.Size = size;
        }
        var x = align switch
        {
            SKTextAlign.Center => bounds.MidX - shaper.Shape(text, font).Width / 2f,
            SKTextAlign.Right => bounds.Right - shaper.Shape(text, font).Width,
            _ => bounds.Left
        };
        var baseline = bounds.Top + Math.Min(bounds.Height - 2, font.Size + 2);
        canvas.DrawShapedText(shaper, text, new SKPoint(Math.Max(bounds.Left, x), baseline),
            SKTextAlign.Left, font, paint);
    }

    private static SKBitmap DrawLegacy(PrintDocument doc)
    {
        var hindi = string.Equals(doc.Language, "hi", StringComparison.OrdinalIgnoreCase);
        var regular = PrintFonts.Get(doc.Language, bold: false);
        var bold = PrintFonts.Get(doc.Language, bold: true);

        var rows = ExpandRows(doc);
        var headerH = doc.IsDuplicate ? 150 : 120;
        const int rowH = 32, footerH = 84;
        var pairRows = (int)Math.Ceiling(rows.Count / 2.0);
        var height = headerH + pairRows * rowH + footerH;
        height = ((height + 7) / 8) * 8; // pad to a multiple of 8 rows for clean ESC* banding

        var bmp = new SKBitmap(WidthPx, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        using var titleFont = new SKFont(bold, 26);
        using var subFont = new SKFont(bold, 18);
        using var labelFont = new SKFont(bold, 15);
        using var valueFont = new SKFont(regular, 15);
        using var shaperBold = new SKShaper(bold);
        using var shaperRegular = new SKShaper(regular);

        var y = 8;
        if (!string.IsNullOrWhiteSpace(doc.LogoPath) && File.Exists(doc.LogoPath))
        {
            try
            {
                using var logo = SKBitmap.Decode(doc.LogoPath);
                if (logo != null)
                {
                    var scale = Math.Min(52f / logo.Width, 52f / logo.Height);
                    var destination = new SKRect(10, 4, 10 + logo.Width * scale, 4 + logo.Height * scale);
                    canvas.DrawBitmap(logo, destination, new SKSamplingOptions(SKFilterMode.Linear));
                }
            }
            catch { /* logo is optional; a bad image must never stop operational printing */ }
        }
        DrawCentered(canvas, doc.CompanyName, shaperBold, titleFont, paint, WidthPx, ref y, 30);
        if (!string.IsNullOrWhiteSpace(doc.Address))
            DrawCentered(canvas, doc.Address, shaperRegular, subFont, paint, WidthPx, ref y, 22);

        if (doc.QrValue.HasValue)
        {
            var qrPng = QrCodeHelper.GeneratePng(doc.QrValue.Value.ToString(), 3);
            using var qrBmp = SKBitmap.Decode(qrPng);
            canvas.DrawBitmap(qrBmp, WidthPx - qrBmp.Width - 10, 4, new SKSamplingOptions(SKFilterMode.Nearest));
        }

        y += 4;
        canvas.DrawLine(10, y, WidthPx - 10, y, paint);
        y += 24;
        var title = hindi ? doc.TitleHindi : doc.TitleEnglish;
        canvas.DrawShapedText(shaperBold, title, new SKPoint(10, y), SKTextAlign.Left, subFont, paint);
        if (!string.IsNullOrWhiteSpace(doc.SeasonName))
            canvas.DrawShapedText(shaperRegular, $"{(doc.Language == "hi" ? "पेराई सत्र" : "Season")}: {doc.SeasonName}", new SKPoint(WidthPx - 260, y), SKTextAlign.Left, valueFont, paint);
        y += 12;
        canvas.DrawLine(10, y, WidthPx - 10, y, paint);
        y += 12;
        if (doc.IsDuplicate)
        {
            DrawCentered(canvas, "*** DUPLICATE / REPRINT ***", shaperBold, subFont, paint, WidthPx, ref y, 26);
            canvas.DrawLine(10, y, WidthPx - 10, y, paint);
            y += 10;
        }

        var colWidth = WidthPx / 2;
        for (var i = 0; i < rows.Count; i += 2)
        {
            DrawRow(canvas, rows[i], hindi, shaperBold, shaperRegular, labelFont, valueFont, paint, 10, y, colWidth);
            if (i + 1 < rows.Count)
                DrawRow(canvas, rows[i + 1], hindi, shaperBold, shaperRegular, labelFont, valueFont, paint, colWidth + 10, y, colWidth);
            y += rowH;
        }

        y += 4;
        canvas.DrawLine(10, y, WidthPx - 10, y, paint);
        y += 22;
        canvas.DrawShapedText(shaperRegular, $"{(doc.Language == "hi" ? "द्वारा जनरेट किया गया" : "Generated By")}: {doc.GeneratedByUserName}", new SKPoint(10, y), SKTextAlign.Left, valueFont, paint);
        canvas.DrawShapedText(shaperRegular, $"Print: {doc.PrintDateTime:dd-MM-yyyy HH:mm:ss}", new SKPoint(WidthPx - 330, y), SKTextAlign.Left, valueFont, paint);
        y += 10;
        DrawCentered(canvas, "Warrior Softech", shaperBold, valueFont, paint, WidthPx, ref y, 18);

        canvas.Flush();
        return bmp;
    }

    private static List<PrintRow> ExpandRows(PrintDocument doc)
    {
        var rows = new List<PrintRow>(doc.Rows);
        foreach (var table in doc.Tables.Where(table => table.Columns.Count > 0 && table.Rows.Count > 0))
        {
            rows.Add(new PrintRow(table.TitleHindi, table.TitleEnglish, ""));
            foreach (var detailRow in table.Rows)
            {
                for (var index = 0; index < table.Columns.Count; index++)
                {
                    var column = table.Columns[index];
                    rows.Add(new PrintRow(column.LabelHindi, column.LabelEnglish,
                        index < detailRow.Count ? detailRow[index] : "-"));
                }
            }
        }
        return rows;
    }

    private static void DrawCentered(SKCanvas canvas, string text, SKShaper shaper, SKFont font, SKPaint paint, int width, ref int y, int lineHeight)
    {
        var shaped = shaper.Shape(text, font);
        var x = Math.Max(0, (width - shaped.Width) / 2f);
        canvas.DrawShapedText(shaper, text, new SKPoint(x, y + lineHeight - 6), SKTextAlign.Left, font, paint);
        y += lineHeight;
    }

    private static void DrawRow(SKCanvas canvas, PrintRow row, bool hindi, SKShaper shaperBold, SKShaper shaperRegular,
        SKFont labelFont, SKFont valueFont, SKPaint paint, int x, int y, int columnWidth)
    {
        var label = (hindi ? row.LabelHindi : row.LabelEnglish) + ":";
        canvas.DrawShapedText(shaperBold, label, new SKPoint(x, y + 18), SKTextAlign.Left, labelFont, paint);
        var maxValueWidth = columnWidth - 250; // leave margin before the next column starts
        var value = Truncate(row.Value, shaperRegular, valueFont, maxValueWidth);
        canvas.DrawShapedText(shaperRegular, value, new SKPoint(x + 230, y + 18), SKTextAlign.Left, valueFont, paint);
    }

    /// <summary>Prevents long values (long names/addresses) overlapping the next column - the full
    /// value is always preserved in the stored transaction data, only this narrow printout truncates.</summary>
    private static string Truncate(string text, SKShaper shaper, SKFont font, float maxWidth)
    {
        if (maxWidth <= 0 || shaper.Shape(text, font).Width <= maxWidth) return text;
        var truncated = text;
        while (truncated.Length > 1 && shaper.Shape(truncated + "...", font).Width > maxWidth)
            truncated = truncated[..^1];
        return truncated + "...";
    }

    /// <summary>Epson ESC/P bit-image raster: ESC '3' fixes line spacing to exactly one 8-dot band
    /// (8/72"), then each band is emitted as ESC * 1 nL nH + 1 byte/column (8 vertical bits, MSB=top).</summary>
    private static byte[] ToEscP(SKBitmap bmp, bool fixedHalfPage)
    {
        using var ms = new MemoryStream();
        void W(params byte[] b) => ms.Write(b, 0, b.Length);

        W(0x1B, 0x40);                       // ESC @  - initialize printer
        W(0x1B, 0x33, LineSpacingUnits);      // ESC 3 n - line spacing n/180"

        int width = bmp.Width, height = bmp.Height;
        var nL = (byte)(width & 0xFF);
        var nH = (byte)((width >> 8) & 0xFF);

        for (var band = 0; band < height; band += 8)
        {
            W(0x1B, 0x2A, 0x01, nL, nH); // ESC * 1 nL nH - double density, 8 dots/column
            var line = new byte[width];
            for (var x = 0; x < width; x++)
            {
                byte col = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    var py = band + bit;
                    if (py < height && IsBlack(bmp, x, py)) col |= (byte)(1 << (7 - bit));
                }
                line[x] = col;
            }
            ms.Write(line, 0, line.Length);
            W(0x0D, 0x0A); // CR LF - advances exactly one band (matches the ESC 3 20 line spacing)
        }
        if (!fixedHalfPage)
            W(0x0A, 0x0A, 0x0A, 0x0A); // preserve legacy spacing for non-weighment documents
        return ms.ToArray();
    }

    private static bool IsBlack(SKBitmap bmp, int x, int y)
    {
        var c = bmp.GetPixel(x, y);
        return (c.Red + c.Green + c.Blue) / 3 < 128;
    }
}
