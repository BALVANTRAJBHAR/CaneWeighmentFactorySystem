using QRCoder;

namespace CaneFactory.Infrastructure.Printing;

/// <summary>QR codes carry a safe identifier ONLY (PurchaseId/AdviceNumber/LoanId) - never
/// Aadhaar, bank details, passwords or API keys.</summary>
public static class QrCodeHelper
{
    public static byte[] GeneratePng(string content, int pixelsPerModule)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}
