using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.Infrastructure.Sms;

/// <summary>Queues SMS events - a single fast, idempotent INSERT with zero network calls, so it can
/// never fail or roll back the Weighment/Payment transaction that triggers it (Phase 10 requirement).
/// Idempotent on (EventCode, ReferenceId): a retried transaction never queues a duplicate SMS.</summary>
public class SmsService : ISmsService
{
    private readonly AppDbContext _db;

    public SmsService(AppDbContext db) => _db = db;

    public async Task<bool> QueueAsync(string eventCode, int growerId, string mobileNumber, string referenceId,
        Dictionary<string, string> placeholders)
        => await QueueInternalAsync(eventCode, growerId, null, mobileNumber, referenceId, placeholders);

    public async Task<bool> QueueForPartyAsync(string eventCode, int partyId, string mobileNumber, string referenceId,
        Dictionary<string, string> placeholders)
        => await QueueInternalAsync(eventCode, null, partyId, mobileNumber, referenceId, placeholders);

    public async Task<bool> QueueForOperationalRecipientAsync(string eventCode, string mobileNumber, string referenceId,
        Dictionary<string, string> placeholders)
        => await QueueInternalAsync(eventCode, null, null, mobileNumber, referenceId, placeholders);

    public async Task<bool> QueueRawAsync(string eventCode, int? growerId, int? partyId, string mobileNumber,
        string referenceId, string message)
    {
        if (!IsValidMobile(mobileNumber) || string.IsNullOrWhiteSpace(message)) return false;
        var cfg = await _db.SmsConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        if (cfg == null || !cfg.Enabled) return false;
        if (await _db.SmsLogs.AnyAsync(l => l.EventCode == eventCode && l.ReferenceId == referenceId)) return false;
        try
        {
            _db.SmsLogs.Add(new SmsLog
            {
                EventCode = eventCode,
                GrowerId = growerId,
                PartyId = partyId,
                MobileNumber = mobileNumber.Trim(),
                ReferenceId = referenceId,
                MessageText = message.Trim(),
                Status = "QUEUED",
                AttemptCount = 0,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task<bool> QueueInternalAsync(string eventCode, int? growerId, int? partyId, string mobileNumber,
        string referenceId, Dictionary<string, string> placeholders)
    {
        if (!IsValidMobile(mobileNumber)) return false;

        var cfg = await _db.SmsConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        if (cfg == null || !cfg.Enabled || !IsEventEnabled(cfg, eventCode)) return false;

        if (await _db.SmsLogs.AnyAsync(l => l.EventCode == eventCode && l.ReferenceId == referenceId))
            return false; // already queued once for this exact event - do not duplicate

        var template = await _db.SmsTemplates.FirstOrDefaultAsync(t => t.EventCode == eventCode && t.Language == cfg.Language && t.Enabled && !t.IsDeleted)
            ?? await _db.SmsTemplates.FirstOrDefaultAsync(t => t.EventCode == eventCode && t.Enabled && !t.IsDeleted);
        if (template == null) return false;

        var message = RenderTemplate(template.MessageTemplate, placeholders);

        try
        {
            _db.SmsLogs.Add(new SmsLog
            {
                EventCode = eventCode,
                GrowerId = growerId,
                PartyId = partyId,
                MobileNumber = mobileNumber,
                ReferenceId = referenceId,
                TemplateId = template.Id,
                MessageText = message,
                Status = "QUEUED",
                AttemptCount = 0,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            return false; // unique index race: another request already queued this exact event
        }
    }

    private static string RenderTemplate(string template, Dictionary<string, string> placeholders)
    {
        var msg = template;
        foreach (var (k, v) in placeholders) msg = msg.Replace("{" + k + "}", v);
        return msg;
    }

    private static bool IsValidMobile(string? mobile) =>
        !string.IsNullOrWhiteSpace(mobile) && mobile.Trim().Length == 10 && mobile.Trim().All(char.IsDigit);

    private static bool IsEventEnabled(SmsConfig cfg, string eventCode) => eventCode switch
    {
        "TARE_COMPLETED" => cfg.CanePurchaseSmsEnabled,
        "PAYMENT_COMPLETED" => cfg.CanePaymentSmsEnabled,
        "SALE_PURCHASE_COMPLETED" => cfg.SalePurchaseSmsEnabled,
        _ => true // manual broadcast/test queues remain controlled by master SMS Enabled.
    };
}
