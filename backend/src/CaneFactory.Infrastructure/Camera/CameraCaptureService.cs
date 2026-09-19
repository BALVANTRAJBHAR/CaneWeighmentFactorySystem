using System.Security.Cryptography;
using System.Net.Sockets;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
    private readonly IHostEnvironment _environment;
    private readonly IEnumerable<ICameraCaptureProvider> _providers;
    private readonly ILogger<CameraCaptureService> _log;

    public CameraCaptureService(AppDbContext db, ISecretProtector protector, IAuditService audit,
        IConfiguration config, IHostEnvironment environment, IEnumerable<ICameraCaptureProvider> providers,
        ILogger<CameraCaptureService> log)
    {
        _db = db; _protector = protector; _audit = audit; _config = config; _environment = environment;
        _providers = providers; _log = log;
    }

    // A placeholder image is useful only for a developer's no-camera demo.
    // It must never replace evidence on a factory server, even when an old
    // CANE_CAMERA_SIMULATOR environment variable was accidentally retained.
    private bool SimulatorMode => _environment.IsDevelopment()
        && string.Equals(_config["Camera:SimulatorMode"], "true", StringComparison.OrdinalIgnoreCase);

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
                if (!await IsCameraReachableAsync(cam, ct))
                {
                    result.Error = $"Camera {cam.CameraNumber:D2} is offline at {cam.IpAddress}:{cam.Port}; skipped for evidence capture.";
                    results.Add(result);
                    continue;
                }
                var provider = ResolveProvider(cam.Protocol);
                if (provider == null)
                {
                    result.Error = $"No capture provider registered for protocol '{cam.Protocol}'.";
                    results.Add(result);
                    continue;
                }
                var password = cam.PasswordEncrypted != null ? _protector.Unprotect(cam.PasswordEncrypted) : null;
                using var cameraTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cameraTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                var (ok, bytes, error) = await provider.CaptureAsync(cam, password, cameraTimeout.Token);
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
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                result.Error = $"Camera {cam.CameraNumber:D2} did not return an evidence image within 8 seconds; skipped.";
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

    /// <summary>Captures payment evidence using the dedicated payment camera when one is configured.
    /// The same frame is saved against every paid purchase so each file can be audited by Purchase ID.</summary>
    public async Task<List<CameraCaptureResult>> CaptureForPaymentAsync(int paymentId, int? capturedByUserId, CancellationToken ct = default)
    {
        var results = new List<CameraCaptureResult>();
        if (!await SettingTrueAsync("CameraSystemEnabled") || !await SettingTrueAsync("ImageCaptureEnabled"))
            return results;

        var payment = await _db.Payments.Include(p => p.Season).AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId && !p.IsDeleted, ct);
        if (payment == null) return results;
        var purchaseIds = await _db.PaymentPurchases.AsNoTracking()
            .Where(pp => pp.PaymentId == paymentId).OrderBy(pp => pp.PurchaseId)
            .Select(pp => pp.PurchaseId).ToListAsync(ct);
        if (purchaseIds.Count == 0) return results;

        var configuredCamera = await ResolvePaymentEvidenceCameraAsync(ct);
        var cameras = configuredCamera == null
            ? await _db.Cameras.AsNoTracking().Where(c => !c.IsDeleted && c.Status && c.CaptureEnabled)
                .OrderBy(c => c.CameraNumber).ToListAsync(ct)
            : new List<CameraConfig> { configuredCamera };

        foreach (var cam in cameras)
        {
            var result = new CameraCaptureResult { CameraConfigId = cam.Id, CameraNumber = cam.CameraNumber };
            try
            {
                if (!await IsCameraReachableAsync(cam, ct))
                {
                    result.Error = $"Camera {cam.CameraNumber:D2} is offline at {cam.IpAddress}:{cam.Port}; skipped for payment evidence.";
                    results.Add(result);
                    continue;
                }
                var provider = ResolveProvider(cam.Protocol);
                if (provider == null)
                {
                    result.Error = $"No capture provider registered for protocol '{cam.Protocol}'.";
                    results.Add(result);
                    continue;
                }
                var password = cam.PasswordEncrypted != null ? _protector.Unprotect(cam.PasswordEncrypted) : null;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                var (ok, bytes, error) = await provider.CaptureAsync(cam, password, timeout.Token);
                if (!ok || bytes == null)
                {
                    result.Error = error;
                    results.Add(result);
                    await _audit.LogAsync("ImageCaptureFailed", "CashEvidence", "Payment", paymentId.ToString(),
                        newValue: new { cam.CameraNumber, error }, success: false, failureReason: error);
                    continue;
                }

                foreach (var purchaseId in purchaseIds)
                {
                    var saved = await SavePaymentImageAsync(bytes, payment, purchaseId, cam, capturedByUserId,
                        replaceExisting: false, ct);
                    result.ImageId ??= saved.imageId;
                    result.ImageName ??= saved.imageName;
                }
                result.Success = true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                result.Error = $"Camera {cam.CameraNumber:D2} did not return a payment evidence image within 8 seconds.";
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                _log.LogWarning(ex, "Camera capture failed for payment {PaymentId}", paymentId);
            }
            results.Add(result);
        }
        return results;
    }

    /// <summary>Captures one selected paid purchase from the payment-evidence live-view dialog.
    /// Retake keeps the historical row but marks it inactive before saving the new, immutable file.</summary>
    public async Task<CameraCaptureResult> CaptureForPaymentPurchaseAsync(int paymentId, int purchaseId,
        int? capturedByUserId, bool replaceExisting = false, CancellationToken ct = default)
    {
        var result = new CameraCaptureResult();
        if (!await SettingTrueAsync("CameraSystemEnabled") || !await SettingTrueAsync("ImageCaptureEnabled"))
        {
            result.Error = "Camera system or image capture is disabled in Developer Configuration.";
            return result;
        }

        var payment = await _db.Payments.Include(p => p.Season).AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId && !p.IsDeleted, ct);
        if (payment == null) { result.Error = "Payment not found."; return result; }
        var isPaidPurchase = await _db.PaymentPurchases.AsNoTracking()
            .AnyAsync(pp => pp.PaymentId == paymentId && pp.PurchaseId == purchaseId, ct);
        if (!isPaidPurchase) { result.Error = "The selected purchase does not belong to this payment."; return result; }

        var cam = await ResolvePaymentEvidenceCameraAsync(ct);
        if (cam == null)
        {
            result.Error = "Configure an active Payment Evidence Camera in Developer > Cameras first.";
            return result;
        }
        result.CameraConfigId = cam.Id;
        result.CameraNumber = cam.CameraNumber;
        try
        {
            if (!await IsCameraReachableAsync(cam, ct))
            {
                result.Error = $"Payment evidence camera is offline at {cam.IpAddress}:{cam.Port}.";
                return result;
            }
            var provider = ResolveProvider(cam.Protocol);
            if (provider == null)
            {
                result.Error = $"No capture provider registered for protocol '{cam.Protocol}'.";
                return result;
            }
            var password = cam.PasswordEncrypted != null ? _protector.Unprotect(cam.PasswordEncrypted) : null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var (ok, bytes, error) = await provider.CaptureAsync(cam, password, timeout.Token);
            if (!ok || bytes == null)
            {
                result.Error = error ?? "Camera did not return an image.";
                return result;
            }
            var saved = await SavePaymentImageAsync(bytes, payment, purchaseId, cam, capturedByUserId,
                replaceExisting, ct);
            result.Success = true;
            result.ImageId = saved.imageId;
            result.ImageName = saved.imageName;
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            result.Error = "Payment evidence camera did not return an image within 8 seconds.";
            return result;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Manual payment evidence capture failed for payment {PaymentId}, purchase {PurchaseId}", paymentId, purchaseId);
            result.Error = ex.Message;
            return result;
        }
    }

    public async Task<List<CameraCaptureResult>> CaptureForSalePurchaseAsync(int salePurchaseId, string stage, int? capturedByUserId, CancellationToken ct = default)
    {
        var results = new List<CameraCaptureResult>();
        if (!await SettingTrueAsync("CameraSystemEnabled") || !await SettingTrueAsync("ImageCaptureEnabled")) return results;
        var sale = await _db.SalePurchases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == salePurchaseId, ct);
        if (sale == null) return results;
        var cameras = await _db.Cameras.AsNoTracking().Where(c => !c.IsDeleted && c.Status && c.CaptureEnabled)
            .OrderBy(c => c.CameraNumber).ToListAsync(ct);
        foreach (var cam in cameras)
        {
            var result = new CameraCaptureResult { CameraConfigId = cam.Id, CameraNumber = cam.CameraNumber };
            try
            {
                if (!await IsCameraReachableAsync(cam, ct))
                {
                    result.Error = $"Camera {cam.CameraNumber:D2} is offline at {cam.IpAddress}:{cam.Port}; skipped for evidence capture.";
                    results.Add(result);
                    continue;
                }
                var provider = ResolveProvider(cam.Protocol);
                if (provider == null) { result.Error = $"No capture provider registered for protocol '{cam.Protocol}'."; results.Add(result); continue; }
                var password = cam.PasswordEncrypted != null ? _protector.Unprotect(cam.PasswordEncrypted) : null;
                using var cameraTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cameraTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                var (ok, bytes, error) = await provider.CaptureAsync(cam, password, cameraTimeout.Token);
                if (!ok || bytes == null)
                {
                    result.Error = error;
                    await _audit.LogAsync("ImageCaptureFailed", "Image", "SalePurchase", salePurchaseId.ToString(), newValue: new { cam.CameraNumber, stage, error }, success: false, failureReason: error);
                }
                else
                {
                    var (imageId, imageName) = await SaveSalePurchaseImageAsync(bytes, salePurchaseId, stage, cam, capturedByUserId, ct);
                    result.Success = true; result.ImageId = imageId; result.ImageName = imageName;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                result.Error = $"Camera {cam.CameraNumber:D2} did not return an evidence image within 8 seconds; skipped.";
            }
            catch (Exception ex) { result.Error = ex.Message; _log.LogWarning(ex, "Camera capture failed for SalePurchase {SalePurchaseId}", salePurchaseId); }
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

        // Preview is operational live-view, not evidence capture: it honours the
        // camera-system switch and per-camera live/status settings, but not
        // ImageCaptureEnabled/CaptureEnabled.
        if (!await SettingTrueAsync("CameraSystemEnabled")) { result.Error = "Camera system is disabled."; return result; }
        if (!cam.Status || !cam.LiveViewEnabled) { result.Error = "Live view is disabled for this camera."; return result; }

        var provider = ResolveProvider(cam.Protocol);
        if (provider == null) { result.Error = $"No capture provider registered for protocol '{cam.Protocol}'."; return result; }
        var password = cam.PasswordEncrypted != null ? _protector.Unprotect(cam.PasswordEncrypted) : null;
        // Live preview is a frequent snapshot request, so use the camera's
        // low-latency sub-stream. Purchase/payment evidence capture continues
        // to use the configured Main stream through the other methods above.
        if (cam.Protocol.Equals("RTSP", StringComparison.OrdinalIgnoreCase)
            && cam.RtspUrl == null
            && cam.Vendor is "CPPlus" or "Dahua")
        {
            cam.StreamType = "Sub";
        }
        var (ok, bytes, error) = await provider.CaptureAsync(cam, password, ct);
        result.Success = ok;
        result.Error = error;
        result.ImageBytes = bytes;
        return result;
    }

    private async Task<bool> SettingTrueAsync(string key)
    {
        var s = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
        return s == null || s.Value.Trim().ToUpperInvariant() is "TRUE" or "ON" or "1";
    }

    /// <summary>Fast LAN guard: an offline camera is never allowed to hold up a weighment save.</summary>
    private static async Task<bool> IsCameraReachableAsync(CameraConfig camera, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(900));
            using var client = new TcpClient();
            await client.ConnectAsync(camera.IpAddress, camera.Port, timeout.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Layout: {Root}/WeighmentImage/Cane Weighment/YYYY/MM/DD/PUR-{id}/{STAGE}-yyyyMMdd-HHmmssfff-CAM{NN}-{seq}.jpg.</summary>
    private async Task<(int imageId, string imageName)> SaveImageAsync(byte[] bytes, Purchase purchase, string stage,
        CameraConfig cam, int? capturedByUserId, CancellationToken ct)
    {
        var root = await ResolveWeighmentImageRootAsync(ct);
        var stageTag = stage.ToUpperInvariant();
        var now = DateTime.Now;
        var folder = Path.Combine(root, "Cane Weighment", now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"), $"PUR-{purchase.Id}");
        Directory.CreateDirectory(folder);

        var existingCount = await _db.PurchaseImages.CountAsync(i =>
            i.PurchaseId == purchase.Id && i.CameraId == cam.Id && i.CaptureStage == stageTag, ct);
        var seq = existingCount + 1;
        var fileName = $"{stageTag}-{now:yyyyMMdd-HHmmssfff}-CAM{cam.CameraNumber:D2}-{seq:D2}.jpg";
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

    /// <summary>Payment evidence contract:
    /// {PaymentEvidenceRoot}\\yyyy-MM-dd\\{GrowerCode}-{AdviceNo}-{PurchaseId}-{PaymentId}.jpg.
    /// Retakes receive an R suffix so the replaced file remains available to the audit trail.</summary>
    private async Task<(int imageId, string imageName)> SavePaymentImageAsync(byte[] bytes, Payment payment,
        int purchaseId, CameraConfig cam, int? capturedByUserId, bool replaceExisting, CancellationToken ct)
    {
        var root = await ResolvePaymentEvidenceRootAsync(ct);
        var now = DateTime.Now;
        var folder = Path.Combine(root, now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(folder);

        var existing = await _db.PaymentImages.Where(i => i.PaymentId == payment.Id && i.PurchaseId == purchaseId &&
            i.CameraId == cam.Id).OrderBy(i => i.Id).ToListAsync(ct);
        if (replaceExisting)
        {
            foreach (var prior in existing.Where(i => i.Status)) prior.Status = false;
        }
        var stem = $"{SanitizeFileComponent(payment.GrowerCode)}-{payment.AdviceNumber}-{purchaseId}-{payment.Id}";
        var fileName = existing.Count == 0 ? $"{stem}.jpg" : $"{stem}-R{existing.Count + 1:D2}.jpg";
        var fullPath = Path.Combine(folder, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes, ct);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        var image = new PaymentImage
        {
            PaymentId = payment.Id,
            PurchaseId = purchaseId,
            CameraId = cam.Id,
            ImageName = fileName,
            FilePath = fullPath,
            FileHash = hash,
            CapturedAt = DateTime.UtcNow,
            CapturedBy = capturedByUserId,
            Status = true
        };
        _db.PaymentImages.Add(image);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(replaceExisting ? "ImageRetaken" : "ImageCaptured", "CashEvidence", "Payment", payment.Id.ToString(),
            newValue: new { payment.Id, purchaseId, payment.GrowerCode, payment.AdviceNumber, cam.CameraNumber, fileName });
        return (image.Id, fileName);
    }

    private async Task<(int imageId, string imageName)> SaveSalePurchaseImageAsync(byte[] bytes, int salePurchaseId, string stage,
        CameraConfig cam, int? capturedByUserId, CancellationToken ct)
    {
        var root = await ResolveWeighmentImageRootAsync(ct);
        var now = DateTime.Now; var stageTag = stage.ToUpperInvariant();
        var folder = Path.Combine(root, "SalePurchase Weighment", now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"), $"SP-{salePurchaseId}");
        Directory.CreateDirectory(folder);
        var seq = await _db.SalePurchaseImages.CountAsync(i => i.SalePurchaseId == salePurchaseId && i.CameraId == cam.Id && i.CaptureStage == stageTag, ct) + 1;
        var fileName = $"{stageTag}-{now:yyyyMMdd-HHmmssfff}-CAM{cam.CameraNumber:D2}-{seq:D2}.jpg"; var fullPath = Path.Combine(folder, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes, ct);
        var image = new SalePurchaseImage { SalePurchaseId = salePurchaseId, CameraId = cam.Id, CaptureStage = stageTag,
            ImageName = fileName, FilePath = fullPath, FileHash = Convert.ToHexString(SHA256.HashData(bytes)), CapturedAt = DateTime.UtcNow, CapturedBy = capturedByUserId };
        _db.SalePurchaseImages.Add(image); await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("ImageCaptured", "Image", "SalePurchaseImage", image.Id.ToString(), newValue: new { salePurchaseId, cam.CameraNumber, stageTag, fileName });
        return (image.Id, fileName);
    }

    private async Task<CameraConfig?> ResolvePaymentEvidenceCameraAsync(CancellationToken ct)
    {
        var setting = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == "PaymentEvidenceCameraId", ct);
        if (!int.TryParse(setting?.Value, out var cameraId) || cameraId <= 0) return null;
        return await _db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cameraId && !c.IsDeleted &&
            c.Status && c.CaptureEnabled && c.LiveViewEnabled, ct);
    }

    private async Task<string> ResolvePaymentEvidenceRootAsync(CancellationToken ct)
    {
        var setting = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == "PaymentEvidenceRoot", ct);
        var configured = string.IsNullOrWhiteSpace(setting?.Value) ? @"C:\WeighmentImage\Payment" : setting.Value;
        return WeighmentImagePathResolver.NormalizeConfiguredPath(configured);
    }

    private async Task<string> ResolveWeighmentImageRootAsync(CancellationToken ct)
    {
        var configured = _config["Storage:ImageRoot"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            var setting = await _db.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "ImageStorageRoot", ct);
            configured = setting?.Value;
        }
        return WeighmentImagePathResolver.ResolveRoot(configured);
    }

    private static string SanitizeFileComponent(string value)
    {
        var safe = new string((value ?? string.Empty).Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "UNKNOWN" : safe;
    }

    private static string SanitizeFolder(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
