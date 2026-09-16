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

    public static SKTypeface Get(string language, bool bold) =>
        string.Equals(language, "hi", StringComparison.OrdinalIgnoreCase)
            ? (bold ? DevanagariBold.Value : DevanagariRegular.Value)
            : (bold ? LatinBold.Value : LatinRegular.Value);

    public static string PathFor(string fileName) => Path.Combine(FontDir, fileName);

    private static SKTypeface Load(string fileName)
    {
        var path = Path.Combine(FontDir, fileName);
        return File.Exists(path) ? SKTypeface.FromFile(path) : SKTypeface.Default;
    }
}
