using SkiaSharp;

namespace CaneFactory.Infrastructure.Printing;

/// <summary>Bundled, properly-licensed fonts for print rendering (never depends on the printer's or
/// OS's built-in fonts). Noto Sans Devanagari (OFL-1.1) for Hindi; Tinos (Apache-2.0, metric-compatible
/// Times New Roman substitute) for English/Latin - both freely redistributable.</summary>
public static class PrintFonts
{
    private static readonly string FontDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");

    private static readonly Lazy<SKTypeface> DevanagariRegular = new(() => Load("NotoSansDevanagari-Regular.ttf"));
    private static readonly Lazy<SKTypeface> DevanagariBold = new(() => Load("NotoSansDevanagari-Bold.ttf"));
    private static readonly Lazy<SKTypeface> LatinRegular = new(() => Load("Tinos-Regular.ttf"));
    private static readonly Lazy<SKTypeface> LatinBold = new(() => Load("Tinos-Bold.ttf"));

    private static readonly Lazy<SKTypeface> SansLight = new(() => LoadVariable("NotoSansDevanagari-Variable.ttf", 300, 100));
    private static readonly Lazy<SKTypeface> SansRegular = new(() => LoadVariable("NotoSansDevanagari-Variable.ttf", 400, 100));
    private static readonly Lazy<SKTypeface> SansMedium = new(() => LoadVariable("NotoSansDevanagari-Variable.ttf", 500, 100));
    private static readonly Lazy<SKTypeface> SansBold = new(() => LoadVariable("NotoSansDevanagari-Variable.ttf", 700, 100));
    private static readonly Lazy<SKTypeface> SansCompactLight = new(() => LoadVariable("NotoSansDevanagari-Variable.ttf", 300, 75));
    private static readonly Lazy<SKTypeface> SansCompactRegular = new(() => LoadVariable("NotoSansDevanagari-Variable.ttf", 400, 75));
    private static readonly Lazy<SKTypeface> SansCompactMedium = new(() => LoadVariable("NotoSansDevanagari-Variable.ttf", 500, 75));
    private static readonly Lazy<SKTypeface> SansCompactBold = new(() => LoadVariable("NotoSansDevanagari-Variable.ttf", 700, 75));
    private static readonly Lazy<SKTypeface> SerifLight = new(() => LoadVariable("NotoSerifDevanagari-Variable.ttf", 300, 100));
    private static readonly Lazy<SKTypeface> SerifRegular = new(() => LoadVariable("NotoSerifDevanagari-Variable.ttf", 400, 100));
    private static readonly Lazy<SKTypeface> SerifMedium = new(() => LoadVariable("NotoSerifDevanagari-Variable.ttf", 500, 100));
    private static readonly Lazy<SKTypeface> SerifBold = new(() => LoadVariable("NotoSerifDevanagari-Variable.ttf", 700, 100));
    private static readonly Lazy<SKTypeface> SerifCompactLight = new(() => LoadVariable("NotoSerifDevanagari-Variable.ttf", 300, 75));
    private static readonly Lazy<SKTypeface> SerifCompactRegular = new(() => LoadVariable("NotoSerifDevanagari-Variable.ttf", 400, 75));
    private static readonly Lazy<SKTypeface> SerifCompactMedium = new(() => LoadVariable("NotoSerifDevanagari-Variable.ttf", 500, 75));
    private static readonly Lazy<SKTypeface> SerifCompactBold = new(() => LoadVariable("NotoSerifDevanagari-Variable.ttf", 700, 75));

    public static SKTypeface Get(string language, bool bold, string? style = null,
        string? configuredWeight = null)
    {
        if (!string.IsNullOrWhiteSpace(style))
        {
            var weight = configuredWeight ?? (bold ? "Bold" : "Regular");
            // Respect the selected impact-printer weight for labels as well
            // as values. Previously a "Regular" profile silently promoted
            // headings/labels to Medium, which makes 9-pin Hindi strokes
            // overprint and leaves the ghost-dot halo seen on LX-310 output.
            // Only the legacy no-setting path retains a bold heading.
            return style switch
            {
                // Exact pre-Epson TVS path: Hindi uses the original static Noto
                // Sans Devanagari Regular/Bold files; English uses Tinos
                // Regular/Bold. Do not substitute variable-font outlines.
                "LegacyTVS" => string.Equals(language, "hi", StringComparison.OrdinalIgnoreCase)
                    ? (bold ? DevanagariBold.Value : DevanagariRegular.Value)
                    : (bold ? LatinBold.Value : LatinRegular.Value),
                "SansCompact" => Select(weight, SansCompactLight, SansCompactRegular, SansCompactMedium, SansCompactBold),
                "Serif" => Select(weight, SerifLight, SerifRegular, SerifMedium, SerifBold),
                "SerifCompact" => Select(weight, SerifCompactLight, SerifCompactRegular, SerifCompactMedium, SerifCompactBold),
                // These are portable bundled equivalents, not workstation-dependent Windows
                // fonts. Tinos is metrically compatible with Times New Roman; the compact
                // Noto Sans face is the most reliable monospaced-like option for Hindi too.
                "TimesNewRoman" => Select(weight, SerifLight, SerifRegular, SerifMedium, SerifBold),
                "CourierNew" => Select(weight, SansCompactLight, SansCompactRegular, SansCompactMedium, SansCompactBold),
                "Monospace" => Select(weight, SansCompactLight, SansCompactRegular, SansCompactMedium, SansCompactBold),
                _ => Select(weight, SansLight, SansRegular, SansMedium, SansBold)
            };
        }

        var hindi = string.Equals(language, "hi", StringComparison.OrdinalIgnoreCase);
        if (hindi)
            return bold ? DevanagariBold.Value : DevanagariRegular.Value;
        return bold ? LatinBold.Value : LatinRegular.Value;
    }

    private static SKTypeface Select(string weight, Lazy<SKTypeface> light,
        Lazy<SKTypeface> regular, Lazy<SKTypeface> medium, Lazy<SKTypeface> bold) => weight switch
    {
        "Light" => light.Value,
        "Medium" => medium.Value,
        "Bold" => bold.Value,
        _ => regular.Value
    };

    public static string PathFor(string fileName) => Path.Combine(FontDir, fileName);

    private static SKTypeface Load(string fileName)
    {
        var path = Path.Combine(FontDir, fileName);
        return File.Exists(path) ? SKTypeface.FromFile(path) : SKTypeface.Default;
    }

    private static SKTypeface LoadVariable(string fileName, float weight, float width)
    {
        var path = Path.Combine(FontDir, fileName);
        if (!File.Exists(path)) return SKTypeface.Default;

        var source = SKTypeface.FromFile(path);
        var coordinates = new[]
        {
            new SKFontVariationPositionCoordinate { Axis = SKFourByteTag.Parse("wght"), Value = weight },
            new SKFontVariationPositionCoordinate { Axis = SKFourByteTag.Parse("wdth"), Value = width }
        };
        var clone = source.Clone(coordinates);
        if (clone is null) return source;
        source.Dispose();
        return clone;
    }
}
