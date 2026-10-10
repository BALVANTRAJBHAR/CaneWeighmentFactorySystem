using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace CaneFactory.Infrastructure.Printing;

/// <summary>
/// TVS MSP 270 Classic Plus (9-pin, ESC/P2-compatible) renderer. The slip is first rasterized to a
/// monochrome SKBitmap using proper HarfBuzz text shaping (so Hindi conjuncts/matras render
/// correctly - the printer's own font is never used), then converted to Epson ESC * bit-image
/// graphics. All text is emitted at 120 dpi double density for clean, non-merged glyphs. Fast
/// weighment mode skips blank columns/bands without reducing text density, so preview remains
/// WYSIWYG while the carriage avoids unnecessary travel.
/// </summary>
public class DotMatrixEscPRenderer : IPrintRenderer
{
    public string TargetType => "DotMatrix";

    private const int WidthPx = 960;        // 8" usable width @ 120 dpi (ESC * m=1, double density)
    private const byte DefaultLineSpacingUnits = 20; // configured physical pitch: n/180" per raster band
    private const int SegmentGapAt120Dpi = 20; // seek across >= 1/6" blank gaps outside graphics mode

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
        var layout = GetLayout(doc);
        return (ToEscP(bmp, doc.UseHalfPageDotMatrixLayout || doc.IsDotMatrixCalibrationSheet,
            UseFastBody(doc), layout.LineSpacingUnits),
            "application/octet-stream", "prn");
    }

    private static bool UseFastBody(PrintDocument doc) =>
        doc.UseHalfPageDotMatrixLayout && !doc.IsDotMatrixCalibrationSheet && doc.DotMatrixFastPrint;

    private static SKBitmap Draw(PrintDocument doc) =>
        doc.IsDotMatrixCalibrationSheet
            ? DrawCalibrationPage(doc)
            : doc.UseHalfPageDotMatrixLayout ? DrawHalfPage(doc) : DrawLegacy(doc);

    private static DotMatrixLayout GetLayout(PrintDocument doc)
    {
        var pageLines = Math.Clamp(doc.DotMatrixPageLines <= 0 ? 108 : doc.DotMatrixPageLines, 80, 180);
        if (pageLines % 2 != 0) pageLines--;

        var halfPageLines = doc.DotMatrixHalfPageLines;
        if (halfPageLines is < 40 or > 90 || halfPageLines * 2 != pageLines)
            halfPageLines = pageLines / 2;

        var contentOffset = Math.Clamp(doc.DotMatrixContentStartOffsetLines, 0, 10);
        var postSlipFeed = Math.Clamp(doc.DotMatrixPostSlipFeedLines, 0, 10);
        var tearLine = doc.DotMatrixTearLinePosition;
        if (tearLine <= 0 || tearLine + postSlipFeed != halfPageLines)
            tearLine = halfPageLines - postSlipFeed;
        tearLine = Math.Clamp(tearLine, Math.Max(30, halfPageLines - 10), halfPageLines);
        postSlipFeed = halfPageLines - tearLine;

        // Keep 34 bands available for the longest Cane Final/Sale Final detail set and footer.
        var maxHeader = Math.Max(4, tearLine - contentOffset - 34);
        var header = Math.Clamp(doc.DotMatrixHeaderReservedLines <= 0 ? 9 : doc.DotMatrixHeaderReservedLines,
            4, maxHeader);
        var lineSpacing = (byte)Math.Clamp(
            doc.DotMatrixLineSpacingUnits <= 0 ? DefaultLineSpacingUnits : doc.DotMatrixLineSpacingUnits,
            12, 30);

        return new DotMatrixLayout(pageLines, halfPageLines, header, contentOffset,
            tearLine, postSlipFeed, lineSpacing);
    }

    private readonly record struct DotMatrixLayout(int PageLines, int HalfPageLines,
        int HeaderReservedLines, int ContentStartOffsetLines, int TearLinePosition,
        int PostSlipFeedLines, byte LineSpacingUnits);

    /// <summary>Fixed-height production layout for cane gross/final and sale tare/final.
    /// One render advances exactly half of the configured continuous page, so two consecutive
    /// weighment jobs occupy the two pre-printed sections without state or an intermediate FF.</summary>
    private static SKBitmap DrawHalfPage(PrintDocument doc)
    {
        var hindi = string.Equals(doc.Language, "hi", StringComparison.OrdinalIgnoreCase);
        var regular = PrintFonts.Get(doc.Language, bold: false);
        // Impact-printer raster text is clearer with a normal-weight face. Bold glyphs create
        // adjacent pin strikes that can visually merge on multipart/continuous stationery.
        var bold = regular;
        var rows = ExpandRows(doc);

        var layout = GetLayout(doc);
        var height = layout.HalfPageLines * 8;
        var headerHeight = layout.HeaderReservedLines * 8;
        var tearY = layout.TearLinePosition * 8 - 3;

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
        var y = headerHeight + layout.ContentStartOffsetLines * 8 + 2;
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
        if (!doc.SuppressHeaderSeparator)
            canvas.DrawLine(10, y, WidthPx - 10, y, paint);
        y += 5;

        if (doc.IsDuplicate)
        {
            DrawFittedText(canvas, "*** DUPLICATE / REPRINT ***", shaperBold, bold, 16, 11,
                new SKRect(10, y, WidthPx - 10, y + 22), paint, SKTextAlign.Center);
            y += 24;
        }

        const int footerHeight = 42;
        var footerBottom = tearY - 4;
        var footerY = footerBottom - footerHeight;
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
            new SKRect(10, footerY + 22, WidthPx - 10, footerBottom), paint, SKTextAlign.Center);

        DrawTearLine(canvas, tearY, paint);

        canvas.Flush();
        return bmp;
    }

    private static void DrawTearLine(SKCanvas canvas, int y, SKPaint paint)
    {
        const int left = 10, right = WidthPx - 10, dash = 18, gap = 10;
        for (var x = left; x < right; x += dash + gap)
            canvas.DrawLine(x, y, Math.Min(x + dash, right), y, paint);
    }

    /// <summary>One complete form, printed without FF, for physically matching software feed bands
    /// to the MSP 270 tractor stationery. The output deliberately uses the same ESC/P line spacing
    /// and feed path as production slips.</summary>
    private static SKBitmap DrawCalibrationPage(PrintDocument doc)
    {
        var layout = GetLayout(doc);
        var height = layout.PageLines * 8;
        var bmp = new SKBitmap(WidthPx, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        var regular = PrintFonts.Get("en", bold: false);
        var bold = PrintFonts.Get("en", bold: true);
        using var shaper = new SKShaper(bold);

        void Zone(int topLine, int bottomLine, string label)
        {
            var top = topLine * 8 + 2;
            var bottom = bottomLine * 8 - 2;
            if (bottom <= top) return;
            canvas.DrawRect(new SKRect(14, top, WidthPx - 14, bottom),
                new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1 });
            DrawFittedText(canvas, label, shaper, bold, 14, 9,
                new SKRect(24, Math.Min(bottom - 20, top + 26), WidthPx - 24,
                    Math.Min(bottom, top + 46)), paint, SKTextAlign.Center);
        }

        void Marker(int y, string label, bool dashed = false, bool labelAbove = false)
        {
            y = Math.Clamp(y, 1, height - 2);
            if (dashed) DrawTearLine(canvas, y, paint);
            else canvas.DrawLine(10, y, WidthPx - 10, y, paint);
            var top = labelAbove || y > height - 28 ? y - 23 : y + 2;
            canvas.DrawRect(new SKRect(18, top, WidthPx - 18, top + 20),
                new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill });
            DrawFittedText(canvas, label, shaper, bold, 13, 8,
                new SKRect(22, top, WidthPx - 22, top + 20), paint, SKTextAlign.Center);
        }

        var firstContentLine = layout.HeaderReservedLines + layout.ContentStartOffsetLines;
        var secondStartLine = layout.HalfPageLines;
        var secondContentLine = secondStartLine + firstContentLine;
        var firstTearY = layout.TearLinePosition * 8 - 3;
        var secondTearY = (layout.HalfPageLines + layout.TearLinePosition) * 8 - 3;

        Zone(0, layout.HeaderReservedLines,
            $"PHYSICAL TOF / FIRST HEADER RESERVED: {layout.HeaderReservedLines} lines / CONTENT START: {firstContentLine}");
        Zone(secondStartLine, secondStartLine + layout.HeaderReservedLines,
            $"SECOND HALF TOF: {secondStartLine} / HEADER RESERVED: {layout.HeaderReservedLines} lines / CONTENT START: {secondContentLine}");
        Marker(1, "PHYSICAL TOP OF FORM (mechanically calibrated baseline)");
        Marker(firstContentLine * 8,
            $"FIRST CONTENT START: line {firstContentLine} (header {layout.HeaderReservedLines} + offset {layout.ContentStartOffsetLines})");
        Marker(firstTearY,
            $"FIRST TEAR / HALF BOUNDARY: tear line {layout.TearLinePosition}; next half line {layout.HalfPageLines}",
            true, true);
        Marker(secondContentLine * 8,
            $"SECOND CONTENT START: absolute line {secondContentLine}");
        Marker(secondTearY,
            $"SECOND TEAR / BOTTOM FORM BOUNDARY / NEXT TOF: {layout.PageLines} lines; spacing {layout.LineSpacingUnits}/180 inch",
            true, true);

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
        if (!doc.SuppressHeaderSeparator)
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

    /// <summary>Epson ESC/P bit-image raster: ESC '3' fixes line spacing to exactly one 8-dot band.
    /// Empty vertical bands use grouped paper feeds. Inked bands are split around large horizontal
    /// gaps with ESC '$', so Fast mode seeks over column whitespace without traversing it in graphics
    /// mode. The MSP 270 interprets ESC 3 in 1/216-inch units, so the user-facing physical pitch
    /// (stored in 1/180-inch units) is converted before sending. All glyphs stay in m=1 120-dpi
    /// double density; no horizontal pixel merging is used.</summary>
    private static byte[] ToEscP(SKBitmap bmp, bool fixedHeight, bool optimizeWhitespace,
        byte physicalLineSpacingUnits180)
    {
        using var ms = new MemoryStream();
        void W(params byte[] b) => ms.Write(b, 0, b.Length);
        var printerLineSpacingUnits216 = (byte)Math.Clamp(
            (int)Math.Round(physicalLineSpacingUnits180 * 216d / 180d), 1, 255);

        void FeedBlankBands(int bandCount)
        {
            if (bandCount <= 0) return;

            // ESC 3 accepts one byte. Group as many blank bands as fit in that command.
            // CR keeps the next raster line at the same left edge; restoring normal spacing
            // ensures the following inked band still advances by exactly one 8-dot band.
            W(0x0D);
            while (bandCount > 0)
            {
                var chunkBands = Math.Min(Math.Max(1, 255 / printerLineSpacingUnits216), bandCount);
                W(0x1B, 0x33, (byte)(chunkBands * printerLineSpacingUnits216));
                W(0x0A);
                bandCount -= chunkBands;
            }
            W(0x1B, 0x33, printerLineSpacingUnits216);
        }

        void WriteRasterSegment(byte[] line, int start, int endExclusive, byte graphicsMode)
        {
            // ESC '$' uses 1/60" units. Double-density columns are 1/120", therefore an odd
            // starting column is rounded down and its preceding blank byte is included.
            if (graphicsMode == 1 && start % 2 != 0) start--;
            var absolutePosition = graphicsMode == 0 ? start : start / 2;
            W(0x1B, 0x24, (byte)(absolutePosition & 0xFF),
                (byte)((absolutePosition >> 8) & 0xFF));

            var length = endExclusive - start;
            W(0x1B, 0x2A, graphicsMode, (byte)(length & 0xFF),
                (byte)((length >> 8) & 0xFF));
            ms.Write(line, start, length);
        }

        void WriteSegmentedRaster(byte[] line, byte graphicsMode)
        {
            var minimumGap = SegmentGapAt120Dpi;
            var segmentStart = Array.FindIndex(line, value => value != 0);
            while (segmentStart >= 0)
            {
                var lastInk = segmentStart;
                var scan = segmentStart + 1;
                for (; scan < line.Length; scan++)
                {
                    if (line[scan] != 0)
                    {
                        lastInk = scan;
                        continue;
                    }
                    if (scan - lastInk >= minimumGap) break;
                }

                WriteRasterSegment(line, segmentStart, lastInk + 1, graphicsMode);
                segmentStart = scan >= line.Length
                    ? -1
                    : Array.FindIndex(line, scan, value => value != 0);
            }
        }

        W(0x1B, 0x40);                       // ESC @  - initialize printer
        W(0x1B, 0x33, printerLineSpacingUnits216); // MSP 270 ESC 3 n - n/216"

        int width = bmp.Width, height = bmp.Height;
        var blankBands = 0;

        for (var band = 0; band < height; band += 8)
        {
            const byte graphicsMode = 1; // 120-dpi double density for clean Hindi/Latin text
            var line = new byte[width];
            var hasInk = false;
            for (var outputX = 0; outputX < width; outputX++)
            {
                byte col = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    var py = band + bit;
                    if (py >= height) continue;
                    if (IsBlack(bmp, outputX, py))
                    {
                        col |= (byte)(1 << (7 - bit));
                    }
                }
                line[outputX] = col;
                hasInk |= col != 0;
            }

            if (!hasInk)
            {
                blankBands++;
                continue;
            }

            FeedBlankBands(blankBands);
            blankBands = 0;
            if (optimizeWhitespace)
                WriteSegmentedRaster(line, graphicsMode);
            else
                WriteRasterSegment(line, 0, line.Length, graphicsMode);
            W(0x0D, 0x0A); // CR LF - advances one calibrated physical raster band
        }
        FeedBlankBands(blankBands);
        if (!fixedHeight)
            W(0x0A, 0x0A, 0x0A, 0x0A); // preserve legacy spacing for non-weighment documents
        return ms.ToArray();
    }

    private static bool IsBlack(SKBitmap bmp, int x, int y)
    {
        var c = bmp.GetPixel(x, y);
        return (c.Red + c.Green + c.Blue) / 3 < 128;
    }
}
