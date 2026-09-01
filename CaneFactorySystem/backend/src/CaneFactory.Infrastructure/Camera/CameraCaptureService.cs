using System.Security.Cryptography;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CaneFactory.Infrastructure.Camera;

/// <summary>
/// Orchestrates camera capture for GROSS/TARE weighment and manual re-capture. Best-effort per camera:
/// one camera failing never blocks another camera or the weighment transaction that triggered it.
/// Respects CameraSystemEnabled + ImageCaptureEnabled global switches and per-camera CaptureEnabled.
/// </summary>
public class CameraCaptureService : ICameraCaptureService
{
    private readonly AppDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IAuditService _audit;
    private readonly IConfiguration _config;
    private readonly IEnumerable<ICameraCaptureProvider> _providers;
    private readonly ILogger<CameraCaptureService> _log;

    public CameraCaptureService(AppDbContext db, ISecretProtector protector, IAuditService audit,
        IConfiguration config, IEnumerable<ICameraCaptureProvider> providers, ILogger<CameraCaptureService> log)
    {
        _db = db; _protector = protector; _audit = audit; _config = config; _providers = providers; _log = log;
    }

    private bool SimulatorMode => string.Equals(_config["Camera:SimulatorMode"], "true", StringComparison.OrdinalIgnoreCase);

    private ICameraCaptureProvider? ResolveProvider(string protocol) =>
        SimulatorMode
            ? _providers.FirstOrDefault(p => p.Protocol == "SIMULATOR")
            : _providers.FirstOrDefault(p => p.Protocol == protocol);

    public async Task<List<CameraCaptureResult>> CaptureForPurchaseAsync(int purchaseId, string stage, int? capturedByUserId, CancellationToken ct = default)
    {
        var results = new List<CameraCaptureResult>();
        if (!await SettingTrueAsync("CameraSystemEnabled") || !await SettingTrueAsync("ImageCaptureEnabled"))
            return results;

        var purchase = await _db.Purchases.Include(p => p.Season).AsNoTracking().FirstOrDefaultAsync(p => p.Id == purchaseId, ct);
        if (purchase == null) return results;

        var cameras = await _db.Cameras.AsNoTracking()
            .Where(c => !c.IsDeleted && c.Status && c.CaptureEnabled).OrderBy(c => c.CameraNumber).ToListAsync(ct);

        foreach (var cam in cameras)
        {
            var result = new CameraCaptureResult { CameraConfigId = cam.Id, CameraNumber = cam.CameraNumber };
            try
            {
                var provider = ResolveProvider(cam.Protocol);
                if (provider == null)
                {
                    result.Error = $"No capture provider registered for protocol '{cam.Protocol}'.";
                    results.Add(result);
                    continue;
                }
                var password = cam.PasswordEncrypted != null ? _protector.Unprotect(cam.PasswordEncrypted) : null;
                var (ok, bytes, error) = await provider.CaptureAsync(cam, password, ct);
                if (!ok || bytes == null)
                {
                    result.Error = error;
                    results.Add(result);
                    await _audit.LogAsync("ImageCaptureFailed", "Image", "Purchase", purchaseId.ToString(),
                        newValue: new { cam.CameraNumber, stage, error }, success: false, failureReason: error);
                    continue;
                }
                var (imageId, imageName) = await SaveImageAsync(bytes, purchase, stage, cam, capturedByUserId, ct);
                result.Success = true;
                result.ImageId = imageId;
                result.ImageName = imageName;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                _log.LogWarning(ex, "Camera capture failed for camera {CameraId} on purchase {PurchaseId}", cam.Id, purchaseId);
            }
            results.Add(result);
        }
        return results;
    }

    public async Task<CameraCaptureResult> CaptureSingleAsync(int cameraConfigId, CancellationToken ct = default)
    {
        var cam = await _db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cameraConfigId && !c.IsDeleted, ct);
        var result = new CameraCaptureResult { CameraConfigId = cameraConfigId };
        if (cam == null) { result.Error = "Camera not found."; return result; }
        result.CameraNumber = cam.CameraNumber;

        var provider = ResolveProvider(cam.Protocol);
        if (provider == null) { result.Error = $"No capture provider registered for protocol '{cam.Protocol}'."; return result; }
        var password = cam.PasswordEncrypted != null ? _protector.Unprotect(cam.PasswordEncrypted) : null;
        var (ok, bytes, error) = await provider.CaptureAsync(cam, password, ct);
        result.Success = ok;
        result.Error = error;
        result.ImageBytes = bytes;
        return result;
    }

    private async Task<bool> SettingTrueAsync(string key)
    {
        var s = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
        return s == null || string.Equals(s.Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Layout: {Root}/{Season}/Images/YYYY/MM/DD/PUR-{id}/{STAGE}-CAM{NN}-{seq}.jpg (never a DB BLOB).</summary>
    private async Task<(int imageId, string imageName)> SaveImageAsync(byte[] bytes, Purchase purchase, string stage,
        CameraConfig cam, int? capturedByUserId, CancellationToken ct)
    {
        var root = _config["Storage:ImageRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            var setting = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "ImageStorageRoot", ct);
            root = string.IsNullOrWhiteSpace(setting?.Value) ? Path.Combine(Path.GetTempPath(), "CanePaymentData") : setting!.Value;
        }
        var stageTag = stage.ToUpperInvariant();
        var seasonFolder = SanitizeFolder(purchase.Season?.SeasonName ?? "Default");
        var now = DateTime.Now;
        var folder = Path.Combine(root, seasonFolder, "Images", now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"), $"PUR-{purchase.Id}");
        Directory.CreateDirectory(folder);

        var existingCount = await _db.PurchaseImages.CountAsync(i =>
            i.PurchaseId == purchase.Id && i.CameraId == cam.Id && i.CaptureStage == stageTag, ct);
        var seq = existingCount + 1;
        var fileName = $"{stageTag}-CAM{cam.CameraNumber:D2}-{seq:D2}.jpg";
        var fullPath = Path.Combine(folder, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes, ct);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        var image = new PurchaseImage
        {
            PurchaseId = purchase.Id,
            GrowerId = purchase.GrowerId,
            VillageId = purchase.VillageId,
            CameraId = cam.Id,
            CaptureStage = stageTag,
            ImageName = fileName,
            FilePath = fullPath,
            FileHash = hash,
            CapturedAt = DateTime.UtcNow,
            CapturedBy = capturedByUserId,
            Status = true
        };
        _db.PurchaseImages.Add(image);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("ImageCaptured", "Image", "PurchaseImage", image.Id.ToString(),
            newValue: new { purchase.Id, cam.CameraNumber, stageTag, fileName });
        return (image.Id, fileName);
    }

    private static string SanitizeFolder(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
