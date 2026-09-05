using CaneFactory.API.Auth;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

/// <summary>Developer configuration: weight rules, sound/TTS, cameras, print, SMS, company, system settings.
/// All secrets are AES-256-GCM encrypted at rest and NEVER returned to any client.</summary>
[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    private readonly ISecretProtector _protector;

    public ConfigController(AppDbContext db, IAuditService audit, ICurrentUser current, ISecretProtector protector)
    {
        _db = db; _audit = audit; _current = current; _protector = protector;
    }

    // ---------------------------------------------------------------- WEIGHT RULES
    [HasPermission("WeightRule.View")]
    [HttpGet("weight-rules")]
    public async Task<IActionResult> GetWeightRules() =>
        Ok(await _db.WeightRules.FirstOrDefaultAsync(r => !r.IsDeleted));

    [HasPermission("WeightRule.Configure")]
    [HttpPut("weight-rules")]
    public async Task<IActionResult> UpdateWeightRules([FromBody] WeightRuleConfig src)
    {
        if (src.MinimumWeightQuintal < 0) return BadRequest(new { message = "Minimum weight cannot be negative." });
        if (src.DefaultCuttingPercent is < 0 or > 100 || src.DefaultTaxPercent is < 0 or > 100)
            return BadRequest(new { message = "Cutting/Tax % must be between 0 and 100." });
        var r = await _db.WeightRules.FirstAsync(x => !x.IsDeleted);
        var old = new { r.MinimumWeightQuintal, r.Enabled, r.ApplyToGross, r.ApplyToTare, r.DefaultCuttingPercent, r.DefaultTaxPercent };
        r.MinimumWeightQuintal = Math.Round(src.MinimumWeightQuintal, 2);
        r.Enabled = src.Enabled;
        r.ApplyToCanePurchase = src.ApplyToCanePurchase;
        r.ApplyToSalePurchase = src.ApplyToSalePurchase;
        r.ApplyToGross = src.ApplyToGross;
        r.ApplyToTare = src.ApplyToTare;
        r.DefaultCuttingPercent = Math.Round(src.DefaultCuttingPercent, 2);
        r.DefaultTaxPercent = Math.Round(src.DefaultTaxPercent, 2);
        r.UpdatedAt = DateTime.UtcNow;
        r.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SystemSettingChange", "WeightRule", "WeightRuleConfig", r.Id.ToString(), oldValue: old, newValue: src);
        return Ok(new { message = $"Weight rules saved. Minimum weight: {r.MinimumWeightQuintal:F2} Quintal." });
    }

    // ---------------------------------------------------------------- SOUND / TTS
    [HasPermission("Sound.View")]
    [HttpGet("sound")]
    public async Task<IActionResult> GetSound() => Ok(new
    {
        config = await _db.SoundConfigs.FirstOrDefaultAsync(s => !s.IsDeleted),
        messages = await _db.SoundMessages.Where(m => !m.IsDeleted).ToListAsync()
    });

    [HasPermission("Sound.Configure")]
    [HttpPut("sound")]
    public async Task<IActionResult> UpdateSound([FromBody] SoundConfig src)
    {
        var validModes = new[] { "OFF", "ONCE", "TWICE", "CONTINUOUS" };
        if (!validModes.Contains(src.RepeatMode)) return BadRequest(new { message = "RepeatMode must be OFF/ONCE/TWICE/CONTINUOUS." });
        if (src.RepeatIntervalSeconds < 1) return BadRequest(new { message = "Repeat interval must be at least 1 second." });
        var s = await _db.SoundConfigs.FirstAsync(x => !x.IsDeleted);
        var old = new { s.SoundEnabled, s.Language, s.VoiceVolume, s.SpeechRate, s.RepeatIntervalSeconds, s.RepeatMode };
        s.SoundEnabled = src.SoundEnabled;
        s.Language = src.Language;
        s.VoiceVolume = Math.Clamp(src.VoiceVolume, 0, 100);
        s.SpeechRate = src.SpeechRate;
        s.RepeatIntervalSeconds = src.RepeatIntervalSeconds;
        s.RepeatMode = src.RepeatMode;
        s.UpdatedAt = DateTime.UtcNow;
        s.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SystemSettingChange", "Sound", "SoundConfig", s.Id.ToString(), oldValue: old, newValue: src);
        return Ok(new { message = "Sound configuration saved successfully." });
    }

    [HasPermission("Sound.Configure")]
    [HttpPut("sound/messages/{id:int}")]
    public async Task<IActionResult> UpdateSoundMessage(int id, [FromBody] SoundMessage src)
    {
        var m = await _db.SoundMessages.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (m == null) return NotFound(new { message = "Sound message not found." });
        var old = new { m.MessageText, m.Enabled };
        m.MessageText = src.MessageText;
        m.Enabled = src.Enabled;
        m.UpdatedAt = DateTime.UtcNow;
        m.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SystemSettingChange", "Sound", "SoundMessage", id.ToString(), oldValue: old, newValue: src);
        return Ok(new { message = $"Sound message for '{m.EventCode}' ({m.LanguageCode}) updated." });
    }

    // ---------------------------------------------------------------- CAMERAS (1-6, vendor-abstracted)
    [HasPermission("Camera.View")]
    [HttpGet("cameras")]
    public async Task<IActionResult> GetCameras() =>
        Ok(await _db.Cameras.Where(c => !c.IsDeleted).OrderBy(c => c.CameraNumber)
            .Select(c => new
            {
                c.Id, c.CameraNumber, c.Vendor, c.Model, c.Protocol, c.IpAddress, c.Port, c.Username,
                HasPassword = c.PasswordEncrypted != null, // password itself is never returned
                c.Channel, c.StreamType, c.RtspUrl, c.Resolution, c.Fps,
                c.CaptureEnabled, c.LiveViewEnabled, c.RetentionDays, c.Status
            }).ToListAsync());

    [HasPermission("Camera.Configure")]
    [HttpPost("cameras")]
    public async Task<IActionResult> SaveCamera([FromBody] CameraSaveRequest req)
    {
        if (req.CameraNumber is < 1 or > 6) return BadRequest(new { message = "Camera number must be between 1 and 6." });
        var validVendors = new[] { "Hikvision", "CPPlus", "Dahua", "Uniview", "GenericONVIF", "GenericRTSP" };
        if (!validVendors.Contains(req.Vendor)) return BadRequest(new { message = $"Vendor must be one of: {string.Join(", ", validVendors)}" });

        var cam = await _db.Cameras.FirstOrDefaultAsync(c => c.CameraNumber == req.CameraNumber && !c.IsDeleted);
        var isNew = cam == null;
        cam ??= new CameraConfig { CameraNumber = req.CameraNumber, CreatedBy = _current.UserId };
        cam.Vendor = req.Vendor; cam.Model = req.Model; cam.Protocol = req.Protocol;
        cam.IpAddress = req.IpAddress; cam.Port = req.Port; cam.Username = req.Username;
        if (!string.IsNullOrEmpty(req.Password)) cam.PasswordEncrypted = _protector.Protect(req.Password);
        cam.Channel = req.Channel; cam.StreamType = req.StreamType; cam.RtspUrl = req.RtspUrl;
        cam.Resolution = req.Resolution; cam.Fps = req.Fps;
        cam.CaptureEnabled = req.CaptureEnabled; cam.LiveViewEnabled = req.LiveViewEnabled;
        cam.RetentionDays = req.RetentionDays; cam.Status = req.Status;
        if (isNew) _db.Cameras.Add(cam);
        else { cam.UpdatedAt = DateTime.UtcNow; cam.UpdatedBy = _current.UserId; }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CameraConfiguration", "Camera", "CameraConfig", cam.Id.ToString(),
            newValue: new { cam.CameraNumber, cam.Vendor, cam.IpAddress, cam.Protocol });
        return Ok(new { message = $"Camera {cam.CameraNumber:D2} ({cam.Vendor}) saved successfully.", id = cam.Id });
    }

    /// <summary>Basic reachability test (TCP connect). Full ONVIF/ISAPI/RTSP capture activates in Phase 6.</summary>
    [HasPermission("Camera.Configure")]
    [HttpPost("cameras/{id:int}/test")]
    public async Task<IActionResult> TestCamera(int id)
    {
        var cam = await _db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (cam == null) return NotFound(new { message = "Camera not found." });
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var task = client.ConnectAsync(cam.IpAddress, cam.Port);
            if (await Task.WhenAny(task, Task.Delay(3000)) != task || !client.Connected)
                return Conflict(new { message = $"Camera {cam.CameraNumber:D2} NOT reachable at {cam.IpAddress}:{cam.Port}." });
            return Ok(new { message = $"Camera {cam.CameraNumber:D2} reachable at {cam.IpAddress}:{cam.Port}." });
        }
        catch (Exception ex)
        {
            return Conflict(new { message = $"Camera test failed: {ex.Message}" });
        }
    }

    /// <summary>Live snapshot preview (Phase 6) - captures one real frame right now via the configured
    /// protocol (RTSP/ONVIF/ISAPI) and streams it back; never persisted as a purchase evidence image.</summary>
    [HasPermission("Camera.Configure")]
    [HttpGet("cameras/{id:int}/snapshot")]
    public async Task<IActionResult> Snapshot(int id, [FromServices] ICameraCaptureService capture, CancellationToken ct)
    {
        var result = await capture.CaptureSingleAsync(id, ct);
        if (!result.Success || result.ImageBytes == null)
            return Conflict(new { message = result.Error ?? "Snapshot capture failed." });
        return File(result.ImageBytes, "image/jpeg");
    }

    // ---------------------------------------------------------------- PRINT
    [HasPermission("Print.View")]
    [HttpGet("print")]
    public async Task<IActionResult> GetPrint() => Ok(await _db.PrintConfigs.FirstOrDefaultAsync(p => !p.IsDeleted));

    [HasPermission("Print.Configure")]
    [HttpPut("print")]
    public async Task<IActionResult> UpdatePrint([FromBody] PrintConfig src)
    {
        var p = await _db.PrintConfigs.FirstAsync(x => !x.IsDeleted);
        var old = new { p.PrinterType, p.PrinterName, p.AutoPrint, p.GrossCopies, p.TareCopies };
        p.PrinterType = src.PrinterType; p.PrinterName = src.PrinterName; p.PaperType = src.PaperType;
        p.AutoPrint = src.AutoPrint;
        p.GrossCopies = Math.Clamp(src.GrossCopies, 0, 5);
        p.TareCopies = Math.Clamp(src.TareCopies, 0, 5);
        p.PaymentCopies = Math.Clamp(src.PaymentCopies, 0, 5);
        p.LoanCopies = Math.Clamp(src.LoanCopies, 0, 5);
        p.SalePurchaseCopies = Math.Clamp(src.SalePurchaseCopies, 0, 5);
        p.Language = src.Language;
        p.UpdatedAt = DateTime.UtcNow;
        p.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("PrintConfiguration", "Print", "PrintConfig", p.Id.ToString(), oldValue: old, newValue: src);
        return Ok(new { message = "Print configuration saved successfully." });
    }

    // ---------------------------------------------------------------- SMS (generic HTTP provider - Phase 10)
    [HasPermission("Sms.View")]
    [HttpGet("sms")]
    public async Task<IActionResult> GetSms()
    {
        var s = await _db.SmsConfigs.FirstOrDefaultAsync(x => !x.IsDeleted);
        var templates = await _db.SmsTemplates.Where(t => !t.IsDeleted).OrderBy(t => t.EventCode).ThenBy(t => t.Language).ToListAsync();
        return Ok(new
        {
            config = s == null ? null : new
            {
                s.Id, s.ProviderName, s.ApiBaseUrl, s.HttpMethod,
                HasApiKey = s.ApiKeyEncrypted != null, HasApiSecret = s.ApiSecretEncrypted != null,
                s.AuthorizationHeader, s.SenderId, s.EntityId, s.Enabled,
                s.Language, s.RequestContentType, s.RequestBodyTemplate, s.ResponseSuccessPath, s.ResponseSuccessValue, s.SalePurchaseRecipients
            },
            templates
        });
    }

    [HasPermission("Sms.Configure")]
    [HttpPut("sms")]
    public async Task<IActionResult> UpdateSms([FromBody] SmsSaveRequest req)
    {
        var s = await _db.SmsConfigs.FirstOrDefaultAsync(x => !x.IsDeleted);
        var isNew = s == null;
        s ??= new SmsConfig { CreatedBy = _current.UserId };
        s.ProviderName = req.ProviderName; s.ApiBaseUrl = req.ApiBaseUrl; s.HttpMethod = req.HttpMethod;
        if (!string.IsNullOrEmpty(req.ApiKey)) s.ApiKeyEncrypted = _protector.Protect(req.ApiKey);
        if (!string.IsNullOrEmpty(req.ApiSecret)) s.ApiSecretEncrypted = _protector.Protect(req.ApiSecret);
        s.AuthorizationHeader = req.AuthorizationHeader; s.SenderId = req.SenderId;
        s.EntityId = req.EntityId; s.Enabled = req.Enabled;
        if (req.Language is "hi" or "en") s.Language = req.Language;
        s.RequestContentType = string.IsNullOrWhiteSpace(req.RequestContentType) ? "application/json" : req.RequestContentType;
        s.RequestBodyTemplate = req.RequestBodyTemplate;
        s.ResponseSuccessPath = req.ResponseSuccessPath;
        s.ResponseSuccessValue = req.ResponseSuccessValue;
        s.SalePurchaseRecipients = req.SalePurchaseRecipients;
        if (isNew) _db.SmsConfigs.Add(s);
        else { s.UpdatedAt = DateTime.UtcNow; s.UpdatedBy = _current.UserId; }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SystemSettingChange", "Sms", "SmsConfig", s.Id.ToString(),
            newValue: new { s.ProviderName, s.ApiBaseUrl, s.Enabled, s.Language }); // secrets never audited in plaintext
        return Ok(new { message = $"SMS configuration for provider '{s.ProviderName}' saved. Credentials stored encrypted." });
    }

    /// <summary>Create or update the message template for one EventCode+Language combination.
    /// EventCode selects a known successful business event.</summary>
    [HasPermission("Sms.Configure")]
    [HttpPost("sms/templates")]
    public async Task<IActionResult> SaveSmsTemplate([FromBody] SmsTemplateSaveRequest req)
    {
        var validEvents = new[] { "TARE_COMPLETED", "PAYMENT_COMPLETED", "SALE_PURCHASE_COMPLETED" };
        if (!validEvents.Contains(req.EventCode))
            return BadRequest(new { message = $"eventCode must be one of: {string.Join(", ", validEvents)}" });
        if (req.Language is not ("hi" or "en"))
            return BadRequest(new { message = "language must be 'hi' or 'en'." });
        if (string.IsNullOrWhiteSpace(req.MessageTemplate))
            return BadRequest(new { message = "Message Template is required." });

        var t = await _db.SmsTemplates.FirstOrDefaultAsync(x => x.EventCode == req.EventCode && x.Language == req.Language && !x.IsDeleted);
        var isNew = t == null;
        t ??= new SmsTemplate { EventCode = req.EventCode, Language = req.Language, CreatedBy = _current.UserId };
        t.MessageTemplate = req.MessageTemplate;
        t.DltTemplateId = req.DltTemplateId;
        t.Enabled = req.Enabled;
        if (isNew) _db.SmsTemplates.Add(t);
        else { t.UpdatedAt = DateTime.UtcNow; t.UpdatedBy = _current.UserId; }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SystemSettingChange", "Sms", "SmsTemplate", t.Id.ToString(),
            newValue: new { t.EventCode, t.Language, t.Enabled });
        return Ok(new { message = $"SMS template for {req.EventCode} ({req.Language}) saved.", id = t.Id });
    }

    /// <summary>Lightweight TCP reachability probe against the configured API Base URL - does NOT send
    /// an actual SMS. Never leaks credentials.</summary>
    [HasPermission("Sms.Configure")]
    [HttpPost("sms/test-connection")]
    public async Task<IActionResult> TestSmsConnection()
    {
        var cfg = await _db.SmsConfigs.FirstOrDefaultAsync(x => !x.IsDeleted);
        if (cfg == null || string.IsNullOrWhiteSpace(cfg.ApiBaseUrl))
            return BadRequest(new { message = "Configure the SMS provider API Base URL first." });
        try
        {
            var uri = new Uri(cfg.ApiBaseUrl.Split('{')[0].TrimEnd('?', '&'));
            var port = uri.Port > 0 ? uri.Port : (uri.Scheme == "https" ? 443 : 80);
            using var tcp = new System.Net.Sockets.TcpClient();
            var connectTask = tcp.ConnectAsync(uri.Host, port);
            if (await Task.WhenAny(connectTask, Task.Delay(4000)) != connectTask || !tcp.Connected)
                return Conflict(new { message = $"SMS provider host '{uri.Host}' is NOT reachable." });
            return Ok(new { message = $"SMS provider host '{uri.Host}:{port}' is reachable." });
        }
        catch (Exception ex)
        {
            return Conflict(new { message = $"Could not test connection: {ex.Message}" });
        }
    }

    /// <summary>Sends ONE real test SMS through the configured generic HTTP provider. Never returns
    /// the raw provider response (may echo back credentials/DLT IDs) - only success/failure + message.</summary>
    [HasPermission("Sms.Configure")]
    [HttpPost("sms/test-send")]
    public async Task<IActionResult> TestSendSms([FromBody] SmsTestSendRequest req, [FromServices] ISmsProviderClient provider, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.MobileNumber)) return BadRequest(new { message = "Mobile Number is required." });
        var cfg = await _db.SmsConfigs.FirstOrDefaultAsync(x => !x.IsDeleted);
        if (cfg == null || !cfg.Enabled) return Conflict(new { message = "SMS is not configured/enabled. Save and enable the SMS configuration first." });

        var apiKey = cfg.ApiKeyEncrypted != null ? _protector.Unprotect(cfg.ApiKeyEncrypted) : null;
        var apiSecret = cfg.ApiSecretEncrypted != null ? _protector.Unprotect(cfg.ApiSecretEncrypted) : null;
        var message = string.IsNullOrWhiteSpace(req.Message) ? "This is a test SMS from CaneFactory System." : req.Message;
        var mobile = req.MobileNumber.Trim();
        var (success, _, error) = await provider.SendAsync(cfg, apiKey, apiSecret, mobile, message, ct);

        await _audit.LogAsync(success ? "SmsSent" : "SmsFailed", "Sms", "SmsConfig", cfg.Id.ToString(),
            newValue: new { EventCode = "TEST", MaskedMobile = CaneFactory.Application.Common.SmsMask.Number(mobile) },
            success: success, failureReason: success ? null : error);
        return Ok(new { success, message = success ? "Test SMS sent successfully." : $"Test SMS failed: {error}" });
    }

    // ---------------------------------------------------------------- COMPANY
    [HasPermission("Company.View")]
    [HttpGet("company")]
    public async Task<IActionResult> GetCompany() => Ok(await _db.CompanyConfigs.FirstOrDefaultAsync(c => !c.IsDeleted));

    [HasPermission("Company.Configure")]
    [HttpPut("company")]
    public async Task<IActionResult> UpdateCompany([FromBody] CompanyConfig src)
    {
        if (string.IsNullOrWhiteSpace(src.CompanyName)) return BadRequest(new { message = "Company Name is required." });
        var c = await _db.CompanyConfigs.FirstAsync(x => !x.IsDeleted);
        var old = new { c.CompanyName, c.Address, c.DefaultLanguage, c.ThemeColor };
        c.CompanyName = src.CompanyName.Trim();
        c.Address = src.Address;
        c.LogoPath = src.LogoPath;
        c.DefaultLanguage = src.DefaultLanguage;
        c.ThemeColor = src.ThemeColor;
        c.UpdatedAt = DateTime.UtcNow;
        c.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SystemSettingChange", "Company", "CompanyConfig", c.Id.ToString(), oldValue: old, newValue: src);
        return Ok(new { message = $"Company '{c.CompanyName}' configuration saved successfully." });
    }

    // ---------------------------------------------------------------- SYSTEM SETTINGS
    [HasPermission("SystemSetting.View")]
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings() => Ok(await _db.SystemSettings.OrderBy(s => s.Key).ToListAsync());

    [HasPermission("SystemSetting.Configure")]
    [HttpPut("settings/{key}")]
    public async Task<IActionResult> UpdateSetting(string key, [FromBody] Dictionary<string, string> body)
    {
        var s = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key);
        if (s == null) return NotFound(new { message = $"Setting '{key}' not found." });
        var old = s.Value;
        s.Value = body.GetValueOrDefault("value") ?? s.Value;
        s.UpdatedAt = DateTime.UtcNow;
        s.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SystemSettingChange", "SystemSetting", "SystemSetting", key, oldValue: old, newValue: s.Value);
        return Ok(new { message = $"Setting '{key}' updated successfully." });
    }
}

public class CameraSaveRequest
{
    public int CameraNumber { get; set; }
    public string Vendor { get; set; } = "Hikvision";
    public string? Model { get; set; }
    public string Protocol { get; set; } = "RTSP";
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 554;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int Channel { get; set; } = 1;
    public string StreamType { get; set; } = "Main";
    public string? RtspUrl { get; set; }
    public string? Resolution { get; set; }
    public int? Fps { get; set; }
    public bool CaptureEnabled { get; set; } = true;
    public bool LiveViewEnabled { get; set; } = true;
    public int RetentionDays { get; set; } = 365;
    public bool Status { get; set; } = true;
}

public class SmsSaveRequest
{
    public string ProviderName { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "POST";
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? AuthorizationHeader { get; set; }
    public string? SenderId { get; set; }
    public string? EntityId { get; set; }
    public bool Enabled { get; set; }
    public string Language { get; set; } = "hi";
    public string? RequestContentType { get; set; }
    public string? RequestBodyTemplate { get; set; }
    public string? ResponseSuccessPath { get; set; }
    public string? ResponseSuccessValue { get; set; }
    public string? SalePurchaseRecipients { get; set; }
}

public class SmsTemplateSaveRequest
{
    public string EventCode { get; set; } = string.Empty;
    public string Language { get; set; } = "hi";
    public string MessageTemplate { get; set; } = string.Empty;
    public string? DltTemplateId { get; set; }
    public bool Enabled { get; set; } = true;
}

public class SmsTestSendRequest
{
    public string MobileNumber { get; set; } = string.Empty;
    public string? Message { get; set; }
}

