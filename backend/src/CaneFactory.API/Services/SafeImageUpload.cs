using SkiaSharp;

namespace CaneFactory.API.Services;

/// <summary>Checks the actual image bytes, strips metadata and trailing content, and scans both
/// the received file and the canonical image before either can be persisted or emailed.</summary>
public static class SafeImageUpload
{
    public sealed record Result(byte[] Bytes, string Extension, string ContentType);

    public static async Task<Result> ValidateAsync(IFormFile file, long maxBytes, long maxPixels,
        IConfiguration configuration, CancellationToken ct)
    {
        if (file == null || file.Length is <= 0 || file.Length > maxBytes)
            throw new InvalidDataException($"Select one image up to {maxBytes / 1_000_000} MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png"))
            throw new InvalidDataException("Only JPG or PNG images are accepted.");

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        if (buffer.Length is <= 0 || buffer.Length > maxBytes)
            throw new InvalidDataException("Image size exceeds the upload limit.");
        var original = buffer.ToArray();
        var jpeg = original.Length >= 3 && original[0] == 0xff && original[1] == 0xd8 && original[2] == 0xff;
        var png = original.Length >= 8 && original.AsSpan(0, 8).SequenceEqual(new byte[]
            { 137, 80, 78, 71, 13, 10, 26, 10 });
        if ((extension is ".jpg" or ".jpeg") != jpeg || (extension == ".png") != png)
            throw new InvalidDataException("Image content does not match its file type.");

        await ScanAsync(original, configuration, ct);
        using var codecStream = new SKMemoryStream(original);
        using var codec = SKCodec.Create(codecStream)
            ?? throw new InvalidDataException("Image format is corrupt or unsupported.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > maxPixels)
            throw new InvalidDataException("Image dimensions exceed the safety limit.");
        using var bitmap = SKBitmap.Decode(original)
            ?? throw new InvalidDataException("Image could not be decoded.");
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, 85)
            ?? throw new InvalidDataException("Image could not be sanitised.");
        var safe = encoded.ToArray();
        if (safe.Length > maxBytes)
            throw new InvalidDataException("Sanitised image exceeds the upload limit.");
        await ScanAsync(safe, configuration, ct);
        return new Result(safe, jpeg ? ".jpg" : ".png", jpeg ? "image/jpeg" : "image/png");
    }

    private static async Task ScanAsync(byte[] bytes, IConfiguration configuration, CancellationToken ct)
    {
        var result = await QuotationMalwareScanner.ScanAsync(bytes, configuration, ct);
        if (result == "INFECTED") throw new InvalidDataException("The image was rejected by the security scanner.");
        if (result is not ("OK" or "SKIPPED"))
            throw new InvalidOperationException("Attachment scanning is unavailable. Try again later.");
    }
}
