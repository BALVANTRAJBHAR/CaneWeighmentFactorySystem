using System.Security.Cryptography;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Camera;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace CaneFactory.API.Controllers;

[ApiController]
[Authorize]
[Route("api/rate-overrides")]
public sealed class RateOverridesController(
    AppDbContext db, ICurrentUser current, ICameraCaptureService capture, IAuditService audit,
    ILogger<RateOverridesController> log) : ControllerBase
{
    private const long MaximumUploadBytes = 6_000_000;
    private const long MaximumDecodedPixels = 40_000_000;

    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        if (!CanUseRateOverride()) return Forbidden();
        var admins = await db.Users.AsNoTracking()
            .Where(u => !u.IsDeleted && u.Status && u.UserRoles.Any(ur => ur.Role.Name == "Admin" && ur.Role.Status && !ur.Role.IsDeleted))
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.Username, u.FullName })
            .ToListAsync(ct);
        var reasons = await db.RateReasons.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status).OrderBy(x => x.ReasonName)
            .Select(x => new { x.Id, x.ReasonName }).ToListAsync(ct);
        var cameraSetting = await db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == "PaymentEvidenceCameraId", ct);
        var camera = int.TryParse(cameraSetting?.Value, out var cameraId)
            ? await db.Cameras.AsNoTracking().Where(x => x.Id == cameraId && !x.IsDeleted && x.Status && x.LiveViewEnabled)
                .Select(x => new { x.Id, x.CameraNumber, x.Vendor, x.Model }).FirstOrDefaultAsync(ct)
            : null;
        return Ok(new { admins, reasons, configuredCamera = camera });
    }

    [HttpPost("evidence/upload")]
    [RequestSizeLimit(MaximumUploadBytes + 100_000)]
    public async Task<IActionResult> Upload([FromForm] string transactionType, [FromForm] int transactionId,
        [FromForm] string? source, [FromForm] IFormFile image, CancellationToken ct)
    {
        if (!CanUseRateOverride()) return Forbidden();
        var type = NormalizeType(transactionType);
        if (type == null) return BadRequest(new { message = "Transaction Type must be CANE or SALE." });
        if (await ValidatePendingTargetAsync(type, transactionId, ct) is { } error) return Conflict(new { message = error });
        if (image == null || image.Length == 0 || image.Length > MaximumUploadBytes)
            return BadRequest(new { message = "Select one JPG/JPEG image up to 6 MB." });
        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg"))
            return BadRequest(new { message = "Only JPG/JPEG rate evidence is allowed." });
        await using var input = image.OpenReadStream();
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory, ct);
        try
        {
            var result = await SaveSanitisedEvidenceAsync(type, transactionId, memory.ToArray(),
                string.Equals(source, "DEVICE_CAMERA", StringComparison.OrdinalIgnoreCase) ? "DEVICE_CAMERA" : "UPLOAD", ct);
            return Ok(result);
        }
        catch (InvalidDataException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("evidence/capture-configured")]
    public async Task<IActionResult> CaptureConfigured([FromBody] RateEvidenceCaptureRequest request, CancellationToken ct)
    {
        if (!CanUseRateOverride()) return Forbidden();
        var type = NormalizeType(request.TransactionType);
        if (type == null) return BadRequest(new { message = "Transaction Type must be CANE or SALE." });
        if (await ValidatePendingTargetAsync(type, request.TransactionId, ct) is { } error) return Conflict(new { message = error });
        var setting = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == "PaymentEvidenceCameraId", ct);
        if (!int.TryParse(setting?.Value, out var cameraId) || cameraId <= 0)
            return Conflict(new { message = "Configure a Payment Evidence Camera in Developer > Cameras first." });
        var frame = await capture.CaptureSingleAsync(cameraId, ct);
        if (!frame.Success || frame.ImageBytes == null)
            return Conflict(new { message = frame.Error ?? "Configured camera did not return an image." });
        try
        {
            var result = await SaveSanitisedEvidenceAsync(type, request.TransactionId, frame.ImageBytes, "CONFIGURED_CAMERA", ct);
            return Ok(result);
        }
        catch (InvalidDataException ex) { return BadRequest(new { message = ex.Message }); }
    }

    private bool CanUseRateOverride() => current.HasPermission("Weighment.Edit") || current.HasPermission("SalePurchase.Edit");
    private ObjectResult Forbidden() => StatusCode(403, new { message = "You do not have permission to attach rate-override evidence." });

    private static string? NormalizeType(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "CANE" => "CANE",
        "SALE" => "SALE",
        _ => null
    };

    private async Task<string?> ValidatePendingTargetAsync(string type, int id, CancellationToken ct)
    {
        if (id <= 0) return "A valid transaction ID is required.";
        if (type == "CANE")
            return await db.Purchases.AnyAsync(x => x.Id == id && !x.IsDeleted && x.GrossTareStatus == "GROSS_DONE", ct)
                ? null : "Cane purchase is not pending Tare.";
        return await db.SalePurchases.AnyAsync(x => x.Id == id && !x.IsDeleted && x.WeighmentStatus == "TARE_PENDING_GROSS", ct)
            ? null : "Sale purchase is not pending Gross.";
    }

    private async Task<object> SaveSanitisedEvidenceAsync(string type, int id, byte[] untrustedBytes, string source, CancellationToken ct)
    {
        if (untrustedBytes.Length is < 4 || untrustedBytes.Length > MaximumUploadBytes ||
            untrustedBytes[0] != 0xFF || untrustedBytes[1] != 0xD8 || untrustedBytes[2] != 0xFF)
            throw new InvalidDataException("The selected file is not a valid JPG/JPEG image.");

        using var decoded = SKBitmap.Decode(untrustedBytes)
            ?? throw new InvalidDataException("The JPG/JPEG image is corrupt or unsupported.");
        if (decoded.Width <= 0 || decoded.Height <= 0 || (long)decoded.Width * decoded.Height > MaximumDecodedPixels)
            throw new InvalidDataException("Image dimensions are invalid or exceed the 40-megapixel safety limit.");
        using var safeImage = SKImage.FromBitmap(decoded);
        using var encoded = safeImage.Encode(SKEncodedImageFormat.Jpeg, 88)
            ?? throw new InvalidDataException("The image could not be sanitised.");
        var safeBytes = encoded.ToArray(); // decode + re-encode strips EXIF/scripts/trailing payloads

        var rootSetting = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == "PaymentEvidenceRoot", ct);
        var root = string.IsNullOrWhiteSpace(rootSetting?.Value)
            ? WeighmentImagePathResolver.NormalizeConfiguredPath(@"C:\WeighmentImage\Payment")
            : WeighmentImagePathResolver.NormalizeConfiguredPath(rootSetting.Value);
        var now = DateTime.Now;
        var folder = Path.Combine(root, "Rate Overrides", now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(folder);
        var token = Guid.NewGuid().ToString("N");
        var imageName = $"{type}-{id}-{token}.jpg";
        var filePath = Path.Combine(folder, imageName);
        await System.IO.File.WriteAllBytesAsync(filePath, safeBytes, ct);
        var evidence = new RateOverrideEvidence
        {
            Token = token, TransactionType = type, TransactionId = id, Source = source,
            ImageName = imageName, FilePath = filePath,
            FileHash = Convert.ToHexString(SHA256.HashData(safeBytes)),
            ExpiresAt = DateTime.UtcNow.AddMinutes(30), CreatedBy = current.UserId
        };
        db.RateOverrideEvidences.Add(evidence);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("EvidenceStaged", "Rate", "RateOverrideEvidence", evidence.Id.ToString(),
            newValue: new { type, transactionId = id, source, evidence.ImageName, evidence.FileHash });
        log.LogInformation("Sanitised rate evidence {EvidenceId} staged for {Type} {TransactionId}", evidence.Id, type, id);
        return new { evidenceToken = token, evidence.ImageName, evidence.Source, evidence.ExpiresAt };
    }
}

public sealed class RateEvidenceCaptureRequest
{
    public string TransactionType { get; set; } = string.Empty;
    public int TransactionId { get; set; }
}
