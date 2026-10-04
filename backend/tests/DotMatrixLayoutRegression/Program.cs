using CaneFactory.Application.DTOs;
using CaneFactory.Infrastructure.Printing;
using SkiaSharp;

const int width = 960;
const int pageLines = 108;
const int halfPageLines = pageLines / 2;
const int halfPagePixels = halfPageLines * 8;
const int expectedRawLength = 5 + halfPageLines * (5 + width + 2);

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

        var raw = renderer.RenderFinal(document).bytes;
        Check(raw.Length == expectedRawLength,
            $"{document.TitleEnglish}: raw ESC/P job contains unexpected feed bytes");

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
Check(physicalPage.Length == expectedRawLength * 2,
    "Two consecutive half-page slips must equal exactly one configured physical page");
File.WriteAllBytes(Path.Combine(outputDirectory, "physical-page-cane-gross-final.prn"), physicalPage);

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
    DotMatrixHeaderReservedLines = 9,
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
