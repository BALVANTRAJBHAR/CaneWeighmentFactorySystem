using SkiaSharp;

namespace CaneFactory.Infrastructure.Storage;

/// <summary>
/// Normalizes camera evidence to a storage-friendly JPEG before either local
/// or Azure Blob persistence. Hashes are calculated from these returned bytes,
/// so integrity checks always describe the exact stored object.
/// </summary>
public static class EvidenceImageOptimizer
{
    private const int MaximumDimension = 1600;
    private const int JpegQuality = 72;

    public static byte[] OptimizeJpeg(byte[] source)
    {
        if (source.Length == 0) return source;
        try
        {
            using var bitmap = SKBitmap.Decode(source);
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0) return source;

            var scale = Math.Min(1d, MaximumDimension / (double)Math.Max(bitmap.Width, bitmap.Height));
            var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

            using var surface = SKSurface.Create(new SKImageInfo(
                width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            if (surface == null) return source;
            surface.Canvas.Clear(SKColors.White);
            using var paint = new SKPaint { IsAntialias = true };
            surface.Canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height), paint);
            surface.Canvas.Flush();

            using var image = surface.Snapshot();
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            var optimized = encoded?.ToArray();
            return optimized is { Length: > 0 } && optimized.Length < source.Length ? optimized : source;
        }
        catch
        {
            // Preserve unusual camera formats instead of losing evidence.
            return source;
        }
    }
}
