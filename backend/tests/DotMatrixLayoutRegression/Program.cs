using CaneFactory.Application.DTOs;
using CaneFactory.Infrastructure.Printing;
using SkiaSharp;

const int width = 960;
const int pageLines = 108;
const int halfPageLines = pageLines / 2;
const int halfPagePixels = halfPageLines * 8;
const int tearLinePosition = 54;
const int unoptimizedRawLength = 5 + halfPageLines * (5 + width + 2);

var renderer = new DotMatrixEscPRenderer();
var outputDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "..", "..", "..", "..", "..", "..", "artifacts", "dotmatrix-tests"));
Directory.CreateDirectory(outputDirectory);

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

var documents = new[]
{
    Build("Cane Gross", 12),
    Build("Cane Final", 22),
    Build("Sale Tare", 13),
    Build("Sale Gross", 17)
};

foreach (var document in documents)
{
    foreach (var printHeader in new[] { true, false })
    {
        document.DotMatrixPrintHeader = printHeader;
        var preview = renderer.RenderPreview(document).bytes;
        using var bitmap = SKBitmap.Decode(preview);
        Check(bitmap.Width == width, $"{document.TitleEnglish}: preview width changed");
        Check(bitmap.Height == halfPagePixels, $"{document.TitleEnglish}: slip is not exactly half-page high");

        var headerBlackPixels = CountBlack(bitmap, 0, document.DotMatrixHeaderReservedLines * 8);
        Check(printHeader ? headerBlackPixels > 50 : headerBlackPixels == 0,
            $"{document.TitleEnglish}: header ON/OFF reserve behavior is incorrect");
        var bodyBlackPixels = CountBlack(bitmap,
            document.DotMatrixHeaderReservedLines * 8 + 1, bitmap.Height);
        Check(bodyBlackPixels > 200,
            $"{document.TitleEnglish}: transaction details disappeared in pre-printed mode");

        var headerBoundaryBlackPixels = CountBlack(bitmap,
            document.DotMatrixHeaderReservedLines * 8,
            document.DotMatrixHeaderReservedLines * 8 + 1);
        Check(headerBoundaryBlackPixels < 10,
            $"{document.TitleEnglish}: a separator intrudes into the header/QR boundary");

        var tearY = document.DotMatrixTearLinePosition * 8 - 3;
        Check(CountBlack(bitmap, tearY, tearY + 1) > 100,
            $"{document.TitleEnglish}: dotted tear line is missing at the calibrated position");
        Check(CountBlack(bitmap, tearY + 1, bitmap.Height) == 0,
            $"{document.TitleEnglish}: transaction ink exists below the tear line");

        var raw = renderer.RenderFinal(document).bytes;
        var feed = InspectEscP(raw);
        Check(feed.TotalBands == halfPageLines,
            $"{document.TitleEnglish}: optimized ESC/P feed changed the half-page height");
        Check(feed.FastFeedBands > 0,
            $"{document.TitleEnglish}: blank raster bands were not converted to fast feed");
        Check(feed.ZeroRasterBands == 0,
            $"{document.TitleEnglish}: an all-white 960-byte raster band is still being sent");
        Check(feed.SingleDensitySegments == 0 && feed.DoubleDensitySegments > 0,
            $"{document.TitleEnglish}: Fast mode reduced text below clean 120-DPI double density");
        Check(feed.PhysicalFeedUnits216 == 6 * 216,
            $"{document.TitleEnglish}: one 54-band slip is not exactly 6 physical inches");
        Check(raw.Length < unoptimizedRawLength,
            $"{document.TitleEnglish}: optimized ESC/P job did not become smaller");
        Check(feed.FormFeeds == 0,
            $"{document.TitleEnglish}: a form-feed was emitted after a half-page slip");
        using var decodedPrint = DecodeEscP(raw, width, halfPagePixels);
        Check(CountBitmapMismatches(bitmap, decodedPrint) == 0,
            $"{document.TitleEnglish}: preview no longer matches segmented Fast ESC/P output");

        var suffix = printHeader ? "header-on" : "header-off";
        File.WriteAllBytes(Path.Combine(outputDirectory,
            $"{document.TitleEnglish.Replace(' ', '-').ToLowerInvariant()}-{suffix}.png"), preview);
    }
}

documents[0].DotMatrixPrintHeader = true;
documents[1].DotMatrixPrintHeader = true;
var firstHalf = renderer.RenderFinal(documents[0]).bytes;
var secondHalf = renderer.RenderFinal(documents[1]).bytes;
var physicalPage = firstHalf.Concat(secondHalf).ToArray();
Check(InspectEscP(firstHalf).TotalBands + InspectEscP(secondHalf).TotalBands == pageLines,
    "Two consecutive optimized half-page slips must advance exactly one configured physical page");
Check(InspectEscP(firstHalf).PhysicalFeedUnits216 + InspectEscP(secondHalf).PhysicalFeedUnits216 == 12 * 216,
    "Two consecutive half-page slips are not exactly one 12-inch physical page");
File.WriteAllBytes(Path.Combine(outputDirectory, "physical-page-cane-gross-final.prn"), physicalPage);

var qualityDocument = Build("Cane Gross", 12);
qualityDocument.DotMatrixFastPrint = false;
qualityDocument.DotMatrixPrintHeader = true;
var qualityFeed = InspectEscP(renderer.RenderFinal(qualityDocument).bytes);
Check(qualityFeed.TotalBands == halfPageLines,
    "Quality fallback changed the configured half-page height");
Check(qualityFeed.SingleDensitySegments == 0 && qualityFeed.DoubleDensitySegments > 0,
    "Quality fallback must remain entirely double-density");

var twentySlipFeed = Enumerable.Range(0, 20)
    .Sum(_ => InspectEscP(renderer.RenderFinal(documents[0]).bytes).TotalBands);
Check(twentySlipFeed == 20 * halfPageLines,
    "Twenty consecutive slips drifted from the deterministic half-page feed cycle");

var calibratedTearDocument = Build("Cane Gross", 12);
calibratedTearDocument.DotMatrixTearLinePosition = 52;
calibratedTearDocument.DotMatrixPostSlipFeedLines = 2;
using (var calibratedTearPreview = SKBitmap.Decode(renderer.RenderPreview(calibratedTearDocument).bytes))
{
    var calibratedTearY = 52 * 8 - 3;
    Check(CountBlack(calibratedTearPreview, calibratedTearY, calibratedTearY + 1) > 100,
        "Configured tear line did not move to line 52");
    Check(CountBlack(calibratedTearPreview, calibratedTearY + 1, calibratedTearPreview.Height) == 0,
        "Configured post-slip feed area is not blank");
}
Check(InspectEscP(renderer.RenderFinal(calibratedTearDocument).bytes).TotalBands == halfPageLines,
    "Tear line plus post-slip feed no longer advances exactly one half-page");

var customSpacingDocument = Build("Cane Gross", 12);
customSpacingDocument.DotMatrixLineSpacingUnits = 18;
var customPrinterSpacing = ToPrinterUnits216(18);
var customSpacingFeed = InspectEscP(renderer.RenderFinal(customSpacingDocument).bytes, customPrinterSpacing);
Check(customSpacingFeed.TotalBands == halfPageLines,
    "Configurable ESC/P line spacing changed the logical half-page band count");

var calibrationDocument = Build("Calibration", 0);
calibrationDocument.IsDotMatrixCalibrationSheet = true;
calibrationDocument.DotMatrixFastPrint = false;
var calibrationPreviewBytes = renderer.RenderPreview(calibrationDocument).bytes;
using (var calibrationBitmap = SKBitmap.Decode(calibrationPreviewBytes))
{
    Check(calibrationBitmap.Height == pageLines * 8,
        "Calibration sheet is not exactly one complete physical form high");
    Check(CountBlack(calibrationBitmap, tearLinePosition * 8 - 3, tearLinePosition * 8 - 2) > 100,
        "Calibration sheet first half/perforation marker is missing");
    Check(CountBlack(calibrationBitmap, pageLines * 8 - 4, pageLines * 8) > 100,
        "Calibration sheet next-form boundary marker is missing");
}
var calibrationRaw = renderer.RenderFinal(calibrationDocument).bytes;
var calibrationFeed = InspectEscP(calibrationRaw);
Check(calibrationFeed.TotalBands == pageLines,
    "Calibration raw job does not advance exactly one physical form");
Check(calibrationFeed.FormFeeds == 0,
    "Calibration job must not use form-feed");
File.WriteAllBytes(Path.Combine(outputDirectory, "continuous-form-calibration.png"), calibrationPreviewBytes);
File.WriteAllBytes(Path.Combine(outputDirectory, "continuous-form-calibration.prn"), calibrationRaw);

using (var top = SKBitmap.Decode(renderer.RenderPreview(documents[0]).bytes))
using (var bottom = SKBitmap.Decode(renderer.RenderPreview(documents[1]).bytes))
using (var pageBitmap = new SKBitmap(width, halfPagePixels * 2))
using (var pageCanvas = new SKCanvas(pageBitmap))
{
    pageCanvas.Clear(SKColors.White);
    pageCanvas.DrawBitmap(top, new SKRect(0, 0, width, halfPagePixels),
        new SKSamplingOptions(SKFilterMode.Nearest));
    pageCanvas.DrawBitmap(bottom, new SKRect(0, halfPagePixels, width, halfPagePixels * 2),
        new SKSamplingOptions(SKFilterMode.Nearest));
    pageCanvas.Flush();
    using var pageImage = SKImage.FromBitmap(pageBitmap);
    using var pagePng = pageImage.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(Path.Combine(outputDirectory, "physical-page-preview.png"), pagePng.ToArray());
}

Console.WriteLine($"PASS: {checks} dot-matrix layout/feed checks across 8 header/stage combinations.");
Console.WriteLine($"Artifacts: {outputDirectory}");

static PrintDocument Build(string title, int rowCount) => new()
{
    Language = "hi",
    TitleHindi = title switch
    {
        "Cane Gross" => "गन्ना क्रय पर्ची - सकल तौल",
        "Cane Final" => "गन्ना क्रय पर्ची - अंतिम तौल",
        "Sale Tare" => "बिक्री/खरीद तौल पर्ची - टेयर",
        _ => "बिक्री/खरीद तौल पर्ची - अंतिम"
    },
    TitleEnglish = title,
    CompanyName = "ऑथेंटिक फूड फार्म एलएलपी — LONG COMPANY NAME TEST",
    Address = "101/1, 101/2, खेरी, करेली, नरसिंहपुर, मध्य प्रदेश, 487221",
    SeasonName = "2026-27",
    QrValue = 100001,
    GeneratedByUserName = "developer",
    UseHalfPageDotMatrixLayout = true,
    DotMatrixPageLines = 108,
    DotMatrixHalfPageLines = 54,
    DotMatrixHeaderReservedLines = 9,
    DotMatrixContentStartOffsetLines = 0,
    DotMatrixTearLinePosition = 54,
    DotMatrixPostSlipFeedLines = 0,
    DotMatrixNextFormTofLines = 108,
    DotMatrixLineSpacingUnits = 20,
    DotMatrixFastPrint = true,
    Rows = Enumerable.Range(1, rowCount).Select(index => new PrintRow(
        index % 2 == 0 ? "किसान का नाम" : "अन्य कटौती वजन (क्विंटल)",
        index % 2 == 0 ? "Grower Name" : "Other Deduction Weight (Qtl)",
        index % 2 == 0 ? "अत्यंत लंबा किसान नाम परीक्षण कुमार सिंह" : $"{index * 1.23:F2}"))
        .ToList()
};

static int CountBlack(SKBitmap bitmap, int startY, int endY)
{
    var count = 0;
    for (var y = Math.Max(0, startY); y < Math.Min(bitmap.Height, endY); y++)
    for (var x = 0; x < bitmap.Width; x++)
    {
        var color = bitmap.GetPixel(x, y);
        if ((color.Red + color.Green + color.Blue) / 3 < 128) count++;
    }
    return count;
}

static int CountBitmapMismatches(SKBitmap expected, SKBitmap actual)
{
    if (expected.Width != actual.Width || expected.Height != actual.Height) return int.MaxValue;
    var mismatches = 0;
    for (var y = 0; y < expected.Height; y++)
    for (var x = 0; x < expected.Width; x++)
    {
        var expectedColor = expected.GetPixel(x, y);
        var actualColor = actual.GetPixel(x, y);
        var expectedBlack = (expectedColor.Red + expectedColor.Green + expectedColor.Blue) / 3 < 128;
        var actualBlack = (actualColor.Red + actualColor.Green + actualColor.Blue) / 3 < 128;
        if (expectedBlack != actualBlack) mismatches++;
    }
    return mismatches;
}

static SKBitmap DecodeEscP(byte[] raw, int width, int height)
{
    var bitmap = new SKBitmap(width, height);
    bitmap.Erase(SKColors.White);
    var index = 0;
    var spacingUnits = ToPrinterUnits216(20);
    var absolutePosition60Dpi = 0;
    var y = 0;

    while (index < raw.Length)
    {
        if (index + 1 < raw.Length && raw[index] == 0x1B && raw[index + 1] == 0x40)
        {
            index += 2;
            continue;
        }
        if (index + 2 < raw.Length && raw[index] == 0x1B && raw[index + 1] == 0x33)
        {
            spacingUnits = raw[index + 2];
            index += 3;
            continue;
        }
        if (index + 3 < raw.Length && raw[index] == 0x1B && raw[index + 1] == 0x24)
        {
            absolutePosition60Dpi = raw[index + 2] | (raw[index + 3] << 8);
            index += 4;
            continue;
        }
        if (index + 4 < raw.Length && raw[index] == 0x1B && raw[index + 1] == 0x2A)
        {
            var graphicsMode = raw[index + 2];
            var rasterWidth = raw[index + 3] | (raw[index + 4] << 8);
            var dataStart = index + 5;
            var horizontalScale = graphicsMode == 0 ? 2 : 1;
            var startX = absolutePosition60Dpi * 2;
            for (var column = 0; column < rasterWidth; column++)
            {
                var dots = raw[dataStart + column];
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((dots & (1 << (7 - bit))) == 0) continue;
                    for (var xOffset = 0; xOffset < horizontalScale; xOffset++)
                    {
                        var x = startX + column * horizontalScale + xOffset;
                        var dotY = y + bit;
                        if (x >= 0 && x < width && dotY >= 0 && dotY < height)
                            bitmap.SetPixel(x, dotY, SKColors.Black);
                    }
                }
            }
            index = dataStart + rasterWidth;
            continue;
        }
        if (raw[index] == 0x0D)
        {
            absolutePosition60Dpi = 0;
            index++;
            continue;
        }
        if (raw[index] == 0x0A)
        {
            y += spacingUnits / ToPrinterUnits216(20) * 8;
            index++;
            continue;
        }
        throw new InvalidOperationException($"Unexpected ESC/P byte 0x{raw[index]:X2} while decoding.");
    }
    return bitmap;
}

static EscPFeedResult InspectEscP(byte[] raw, int baseSpacingUnits = 24)
{
    var index = 0;
    var spacingUnits = ToPrinterUnits216(20);
    var rasterFeedPending = false;
    var rasterBands = 0;
    var fastFeedBands = 0;
    var zeroRasterBands = 0;
    var singleDensitySegments = 0;
    var doubleDensitySegments = 0;
    var rasterPayloadBytes = 0;
    var formFeeds = 0;
    var physicalFeedUnits216 = 0;

    while (index < raw.Length)
    {
        if (index + 1 < raw.Length && raw[index] == 0x1B && raw[index + 1] == 0x40)
        {
            index += 2;
            continue;
        }
        if (index + 2 < raw.Length && raw[index] == 0x1B && raw[index + 1] == 0x33)
        {
            spacingUnits = raw[index + 2];
            index += 3;
            continue;
        }
        if (index + 3 < raw.Length && raw[index] == 0x1B && raw[index + 1] == 0x24)
        {
            index += 4;
            continue;
        }
        if (index + 4 < raw.Length && raw[index] == 0x1B && raw[index + 1] == 0x2A)
        {
            var graphicsMode = raw[index + 2];
            var rasterWidth = raw[index + 3] | (raw[index + 4] << 8);
            if (rasterWidth <= 0)
                throw new InvalidOperationException("ESC/P raster segment has no columns.");
            if (graphicsMode == 0) singleDensitySegments++;
            else if (graphicsMode == 1) doubleDensitySegments++;
            else throw new InvalidOperationException($"Unexpected ESC/P graphics mode {graphicsMode}.");
            var dataStart = index + 5;
            if (dataStart + rasterWidth > raw.Length)
                throw new InvalidOperationException("Truncated ESC/P raster payload.");
            if (raw.AsSpan(dataStart, rasterWidth).IndexOfAnyExcept((byte)0) < 0)
                zeroRasterBands++;
            rasterPayloadBytes += rasterWidth;
            rasterFeedPending = true;
            index = dataStart + rasterWidth;
            continue;
        }
        if (raw[index] == 0x0D)
        {
            index++;
            continue;
        }
        if (raw[index] == 0x0A)
        {
            if (spacingUnits % baseSpacingUnits != 0)
                throw new InvalidOperationException($"Non-integral band feed: {spacingUnits}/216 inch.");
            var bands = spacingUnits / baseSpacingUnits;
            physicalFeedUnits216 += spacingUnits;
            if (rasterFeedPending)
            {
                rasterBands += bands;
                rasterFeedPending = false;
            }
            else
            {
                fastFeedBands += bands;
            }
            index++;
            continue;
        }
        if (raw[index] == 0x0C)
        {
            formFeeds++;
            index++;
            continue;
        }
        throw new InvalidOperationException($"Unexpected ESC/P byte 0x{raw[index]:X2} at offset {index}.");
    }

    if (rasterFeedPending)
        throw new InvalidOperationException("Raster command was not followed by a line feed.");
    return new EscPFeedResult(rasterBands + fastFeedBands, fastFeedBands, zeroRasterBands,
        singleDensitySegments, doubleDensitySegments, rasterPayloadBytes, formFeeds,
        physicalFeedUnits216);
}

static int ToPrinterUnits216(int physicalUnits180) =>
    (int)Math.Round(physicalUnits180 * 216d / 180d);

readonly record struct EscPFeedResult(int TotalBands, int FastFeedBands, int ZeroRasterBands,
    int SingleDensitySegments, int DoubleDensitySegments, int RasterPayloadBytes, int FormFeeds,
    int PhysicalFeedUnits216);
