using CaneFactory.Application.Common;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

/// <summary>Read-only queue/log viewer + manual retry for Phase 10 SMS delivery. Mobile numbers are
/// ALWAYS masked in responses - the full number is only ever used server-side to call the provider.</summary>
[ApiController]
[Authorize]
[Route("api/sms-logs")]
public class SmsLogController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAuditService _audit;

    public SmsLogController(AppDbContext db, ICurrentUser current, IAuditService audit)
    {
        _db = db; _current = current; _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? eventCode,
        [FromQuery] int? growerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (!_current.HasPermission("Sms.View"))
            return StatusCode(403, new { message = "You do not have 'Sms.View' permission." });

        var q = _db.SmsLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(l => l.Status == status);
        if (!string.IsNullOrWhiteSpace(eventCode)) q = q.Where(l => l.EventCode == eventCode);
        if (growerId.HasValue) q = q.Where(l => l.GrowerId == growerId);
        var total = await q.CountAsync();
        var raw = await q.OrderByDescending(l => l.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var items = raw.Select(l => new
        {
            l.Id, l.EventCode, l.GrowerId, l.ReferenceId, l.MessageText, l.Status, l.AttemptCount,
            l.SentAt, l.FailureReason, l.CreatedAt, l.NextAttemptAt,
            mobileMasked = SmsMask.Number(l.MobileNumber)
        });
        return Ok(new { items, totalCount = total, page, pageSize });
    }

    /// <summary>Forces a FAILED (or already RETRY_PENDING) entry to be attempted again immediately
    /// on the next SmsQueueProcessor cycle, instead of waiting out its backoff timer.</summary>
    [HttpPost("{id:int}/retry")]
    public async Task<IActionResult> Retry(int id)
    {
        if (!_current.HasPermission("Sms.Configure"))
            return StatusCode(403, new { message = "You do not have 'Sms.Configure' permission." });

        var log = await _db.SmsLogs.FirstOrDefaultAsync(l => l.Id == id);
        if (log == null) return NotFound(new { message = "SMS log entry not found." });
        if (log.Status is not ("FAILED" or "RETRY_PENDING"))
            return Conflict(new { message = $"SMS {id} is {log.Status} and cannot be retried." });

        log.Status = "RETRY_PENDING";
        log.NextAttemptAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SmsFailed", "Sms", "SmsLog", id.ToString(), newValue: new { Status = "RETRY_PENDING (manual)" });
        return Ok(new { message = $"SMS {id} queued for immediate retry." });
    }
}
