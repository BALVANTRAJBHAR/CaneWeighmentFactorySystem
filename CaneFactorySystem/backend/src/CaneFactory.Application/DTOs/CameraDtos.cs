namespace CaneFactory.Application.DTOs;

/// <summary>Per-camera outcome of a Gross/Tare/manual capture attempt.</summary>
public class CameraCaptureResult
{
    public int CameraConfigId { get; set; }
    public int CameraNumber { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int? ImageId { get; set; }
    public string? ImageName { get; set; }

    /// <summary>Populated only for the single-camera developer preview endpoint - never persisted twice.</summary>
    public byte[]? ImageBytes { get; set; }
}
