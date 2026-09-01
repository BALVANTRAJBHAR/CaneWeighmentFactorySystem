using CaneFactory.Domain.Common;

namespace CaneFactory.Domain.Entities;

public class WeighingDevice : BaseEntity
{
    public string DeviceName { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? ModelNumber { get; set; }
    public string ConnectionType { get; set; } = "RS232";
    public string ComPort { get; set; } = "COM1";
    public int BaudRate { get; set; } = 2400;
    public string Parity { get; set; } = "None";
    public int DataBits { get; set; } = 8;
    public int StopBits { get; set; } = 1;
    public string FlowControl { get; set; } = "None";
    public string CharacterEncoding { get; set; } = "ASCII";
    public int ReadTimeoutMs { get; set; } = 1000;
    public int ReadIntervalMs { get; set; } = 200;
    public bool AutoReconnect { get; set; } = true;
    public int ReconnectAttempts { get; set; } = 5;
    public bool IsEnabled { get; set; } = true;
    public bool ActiveConfiguration { get; set; }
    public int? ActiveStringProfileId { get; set; }
    public StringProfile? ActiveStringProfile { get; set; }
}

public class StringProfile : BaseEntity
{
    public string StringProfileName { get; set; } = string.Empty;
    public string StringType { get; set; } = string.Empty;
    public string ParserType { get; set; } = "FixedPosition";
    public string? StartByte { get; set; }
    public string? SignByte { get; set; }
    public int? SignPosition { get; set; }
    public int WeightStartPosition { get; set; }
    public int WeightLength { get; set; } = 6;
    public string WeightCharacterOrder { get; set; } = "Normal";
    public int? DecimalPosition { get; set; }
    public int DecimalPlaces { get; set; }
    public string WeightUnit { get; set; } = "KG";
    public string? EndByte { get; set; }
    public bool CarriageReturn { get; set; } = true;
    public bool LineFeed { get; set; } = true;
    public string PositiveSignValue { get; set; } = "0x20";
    public string NegativeSignValue { get; set; } = "0x2D";
    public string StableWeightRule { get; set; } = "SameValueDuration";
    public int StableWeightDurationMs { get; set; } = 1500;
    public bool FrameValidation { get; set; } = true;
    public string InvalidFrameHandling { get; set; } = "Ignore";
    public string? Delimiter { get; set; }
    public string? RegexPattern { get; set; }
    public string? KeyName { get; set; }
    public string? JsonPath { get; set; }
}

public class DeviceConfigHistory
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? OldConfigJson { get; set; }
    public string? NewConfigJson { get; set; }
    public int? ChangedBy { get; set; }
    public string? ChangedByName { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? Result { get; set; }
}

public class WeightRuleConfig : BaseEntity
{
    public decimal MinimumWeightQuintal { get; set; } = 10.00m;
    public bool Enabled { get; set; } = true;
    public bool ApplyToCanePurchase { get; set; } = true;
    public bool ApplyToSalePurchase { get; set; } = true;
    public bool ApplyToGross { get; set; } = true;
    public bool ApplyToTare { get; set; } = true;
    public decimal DefaultCuttingPercent { get; set; }
    public decimal DefaultTaxPercent { get; set; }
}

public class SoundConfig : BaseEntity
{
    public bool SoundEnabled { get; set; } = true;
    public string Language { get; set; } = "hi";
    public int VoiceVolume { get; set; } = 100;
    public decimal SpeechRate { get; set; } = 1.0m;
    public int RepeatIntervalSeconds { get; set; } = 10;
    public string RepeatMode { get; set; } = "CONTINUOUS";
}

public class SoundMessage : BaseEntity
{
    public string EventCode { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "hi";
    public string MessageText { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
