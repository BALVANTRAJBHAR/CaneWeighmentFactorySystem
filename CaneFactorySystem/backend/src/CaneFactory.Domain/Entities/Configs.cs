using CaneFactory.Domain.Common;

namespace CaneFactory.Domain.Entities;

public class CameraConfig : BaseEntity
{
    public int CameraNumber { get; set; }
    public string Vendor { get; set; } = "Hikvision";
    public string? Model { get; set; }
    public string Protocol { get; set; } = "RTSP";
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 554;
    public string? Username { get; set; }
    public string? PasswordEncrypted { get; set; }
    public int Channel { get; set; } = 1;
    public string StreamType { get; set; } = "Main";
    public string? RtspUrl { get; set; }
    public string? OnvifSettings { get; set; }
    public string? IsapiSettings { get; set; }
    public string? Resolution { get; set; }
    public int? Fps { get; set; }
    public bool CaptureEnabled { get; set; } = true;
    public bool LiveViewEnabled { get; set; } = true;
    public int RetentionDays { get; set; } = 365;
}

public class PrintConfig : BaseEntity
{
    public string PrinterType { get; set; } = "DotMatrix";
    public string PrinterName { get; set; } = string.Empty;
    public string PaperType { get; set; } = "Continuous";
    public bool AutoPrint { get; set; } = true;
    public int GrossCopies { get; set; } = 1;
    public int TareCopies { get; set; } = 2;
    public int PaymentCopies { get; set; } = 1;
    public int LoanCopies { get; set; } = 1;
    public int SalePurchaseCopies { get; set; } = 2;
    public string Language { get; set; } = "hi";
}

public class SmsConfig : BaseEntity
{
    public string ProviderName { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "POST";
    public string? ApiKeyEncrypted { get; set; }
    public string? ApiSecretEncrypted { get; set; }
    public string? AuthorizationHeader { get; set; }
    public string? SenderId { get; set; }
    public string? EntityId { get; set; }
    public bool Enabled { get; set; }
}

public class SmsTemplate : BaseEntity
{
    public string EventCode { get; set; } = string.Empty;
    public string? DltTemplateId { get; set; }
    public string MessageTemplate { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public class RazorpayConfig : BaseEntity
{
    public bool Enabled { get; set; }
    public string Mode { get; set; } = "Test";
    public string? KeyIdEncrypted { get; set; }
    public string? KeySecretEncrypted { get; set; }
    public string? WebhookSecretEncrypted { get; set; }
    public string? AccountNumber { get; set; }
}

public class SystemSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

public class NumberSequence
{
    public string Name { get; set; } = string.Empty;
    public long NextValue { get; set; }
}
