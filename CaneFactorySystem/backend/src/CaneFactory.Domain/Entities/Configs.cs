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
    public string DotMatrixPrinterName { get; set; } = string.Empty;
    public string A4PrinterName { get; set; } = string.Empty;
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

    // Generic HTTP request/response mapping (no provider hard-coded - works with any HTTP SMS gateway)
    public string Language { get; set; } = "hi"; // hi = default, switchable to en
    public string RequestContentType { get; set; } = "application/json";
    public string? RequestBodyTemplate { get; set; } // placeholders: {Mobile} {Message} {ApiKey} {ApiSecret} {SenderId} {EntityId}
    public string? ResponseSuccessPath { get; set; } // dotted JSON field path, e.g. "status"; empty = any 2xx is success
    public string? ResponseSuccessValue { get; set; } // expected value at ResponseSuccessPath, e.g. "success"
    /// <summary>Comma-separated operational recipients for completed SalePurchase weighments.</summary>
    public string? SalePurchaseRecipients { get; set; }
}

public class SmsTemplate : BaseEntity
{
    public string EventCode { get; set; } = string.Empty; // TARE_COMPLETED | PAYMENT_COMPLETED
    public string Language { get; set; } = "hi"; // hi | en
    public string? DltTemplateId { get; set; }
    public string MessageTemplate { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

/// <summary>SMS delivery queue/log (Phase 10) - one row per event. Never a BaseEntity: this is an
/// internal operational log, not a user-facing business record. Idempotent on (EventCode, ReferenceId)
/// so a retried Weighment/Payment transaction never sends a duplicate SMS.</summary>
public class SmsLog
{
    public int Id { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public int? GrowerId { get; set; }
    public int? PartyId { get; set; }
    public string MobileNumber { get; set; } = string.Empty; // full number stored; masked only in API responses
    public string ReferenceId { get; set; } = string.Empty; // e.g. PUR-105, PAY-12 - idempotency key
    public int? TemplateId { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public string Status { get; set; } = "QUEUED"; // QUEUED | PROCESSING | SENT | FAILED | RETRY_PENDING
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Phase 13: automated SQL Server backup schedule/retention configuration (single row).
/// Actual scheduled execution runs outside the API process via Windows Task Scheduler + sqlcmd
/// (SQL Server 2019 Express has no SQL Agent) - the API only stores the desired policy and
/// generates the matching .sql / Task Scheduler XML from it (see BackupController).</summary>
public class BackupConfig : BaseEntity
{
    public bool Enabled { get; set; } = true;
    public string Frequency { get; set; } = "Daily"; // Daily | Hourly | Weekly
    public string TimeOfDay { get; set; } = "02:00"; // HH:mm (24h), used for Daily/Weekly FULL backup
    public int RetentionDays { get; set; } = 14;
    public string BackupFolderPath { get; set; } = @"E:\Backup";
    public bool DifferentialEnabled { get; set; } = true;
    public int DifferentialIntervalHours { get; set; } = 4;
    public bool TransactionLogEnabled { get; set; } = true;
    public int TransactionLogIntervalMinutes { get; set; } = 30;
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
