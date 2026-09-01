using CaneFactory.Application.DTOs;

namespace CaneFactory.Application.Interfaces;

public interface ICurrentUser
{
    int? UserId { get; }
    string? Username { get; }
    string? Role { get; }
    string? Ip { get; }
    string? Device { get; }
    bool HasPermission(string code);
}

public interface IAuditService
{
    Task LogAsync(string action, string module, string? entity = null, string? entityId = null,
        object? oldValue = null, object? newValue = null, bool success = true, string? failureReason = null);
}

public interface ISequenceGenerator
{
    /// <summary>Reserve the next business serial inside the CURRENT ambient transaction. Number is consumed only on commit.</summary>
    Task<long> NextAsync(string sequenceName, long startValue = 1);
}

public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string cipherText);
    string Hash(string value);
}

public interface ITokenService
{
    (string token, DateTime expiresAt) CreateAccessToken(int userId, string username, IEnumerable<string> roles, IEnumerable<string> permissions);
    string CreateRefreshTokenValue();
}

public interface IWeightFrameParser
{
    ParsedFrameDto Parse(byte[] frame);
}

public interface ILiveWeightBroadcaster
{
    Task BroadcastAsync(LiveWeightDto dto);
}

/// <summary>Vendor-abstracted single-shot snapshot capture for one protocol (RTSP/ONVIF/ISAPI/SIMULATOR).</summary>
public interface ICameraCaptureProvider
{
    string Protocol { get; }
    Task<(bool success, byte[]? jpegBytes, string? error)> CaptureAsync(
        CaneFactory.Domain.Entities.CameraConfig camera, string? plainPassword, CancellationToken ct);
}

/// <summary>Orchestrates capture across all enabled cameras and persists PurchaseImage metadata + files on disk.</summary>
public interface ICameraCaptureService
{
    Task<List<CameraCaptureResult>> CaptureForPurchaseAsync(int purchaseId, string stage, int? capturedByUserId, CancellationToken ct = default);
    Task<List<CameraCaptureResult>> CaptureForPaymentAsync(int paymentId, int? capturedByUserId, CancellationToken ct = default);
    Task<CameraCaptureResult> CaptureSingleAsync(int cameraConfigId, CancellationToken ct = default);
}

/// <summary>One renderer per printer target ("A4" | "DotMatrix"). RenderPreview must be visually
/// equivalent to RenderFinal (WYSIWYG) so the Flutter print-preview screen never lies to the user.</summary>
public interface IPrintRenderer
{
    string TargetType { get; }
    (byte[] bytes, string contentType, string fileExtension) RenderFinal(CaneFactory.Application.DTOs.PrintDocument doc);
    (byte[] bytes, string contentType, string fileExtension) RenderPreview(CaneFactory.Application.DTOs.PrintDocument doc);
}

/// <summary>Builds PrintDocuments from transaction data and dispatches to the configured renderer.
/// Single centralized engine reused by every future print-producing module.</summary>
public interface IPrintEngineService
{
    Task<CaneFactory.Application.DTOs.PrintDocument> BuildGrossSlipAsync(int purchaseId, string generatedByUserName);
    Task<CaneFactory.Application.DTOs.PrintDocument> BuildTareSlipAsync(int purchaseId, string generatedByUserName);
    Task<CaneFactory.Application.DTOs.PrintDocument> BuildLoanSlipAsync(int loanId, string generatedByUserName);
    Task<CaneFactory.Application.DTOs.PrintDocument> BuildLoanRecoverySlipAsync(int loanRecoveryId, string generatedByUserName);
    Task<CaneFactory.Application.DTOs.PrintDocument> BuildPaymentSlipAsync(int paymentId, string generatedByUserName);
    CaneFactory.Application.DTOs.PrintDocument BuildTestDocument(string language, string generatedByUserName);
    (byte[] bytes, string contentType, string fileExtension) Render(CaneFactory.Application.DTOs.PrintDocument doc, string target, bool preview);
}
