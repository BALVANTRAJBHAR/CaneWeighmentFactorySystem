using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

/// <summary>
/// Queues a one-off message to all active growers, or only an operator-selected subset.
/// It deliberately inserts into the existing durable SMS queue: this endpoint never calls
/// an SMS gateway inline, so it cannot hold the UI request open while a provider is slow.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sms-broadcast")]
public class SmsBroadcastController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly ISmsService _sms;
    private readonly IAuditService _audit;

    public SmsBroadcastController(AppDbContext db, ICurrentUser current, ISmsService sms, IAuditService audit)
    {
        _db = db; _current = current; _sms = sms; _audit = audit;
    }

    [HttpGet("growers")]
    public async Task<IActionResult> Growers()
    {
        if (!_current.HasPermission("Sms.Configure"))
            return StatusCode(403, new { message = "You do not have 'Sms.Configure' permission." });
        var items = await _db.Growers.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status && x.Mobile != null && x.Mobile.Length == 10)
            .OrderBy(x => x.GrowerCode)
            .Select(x => new { x.Id, x.GrowerCode, x.GrowerName, x.FatherName, x.Mobile })
            .ToListAsync();
        // The sender must see the selected destination before explicitly sending it. The list is only
        // exposed to an SMS-configure role; routine SMS logs remain masked for everyone else.
        return Ok(new { items, totalCount = items.Count });
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SmsBroadcastRequest req)
    {
        if (!_current.HasPermission("Sms.Configure"))
            return StatusCode(403, new { message = "You do not have 'Sms.Configure' permission." });
        var message = req.Message?.Trim() ?? "";
        if (message.Length is < 1 or > 1000)
            return BadRequest(new { message = "Message is required and must be 1 to 1000 characters." });
        if (!req.SendToAll && (req.GrowerIds == null || req.GrowerIds.Count == 0))
            return BadRequest(new { message = "Select at least one grower or choose Send to All Active Growers." });

        var query = _db.Growers.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status && x.Mobile != null && x.Mobile.Length == 10);
        if (!req.SendToAll)
        {
            var ids = req.GrowerIds!.Where(x => x > 0).Distinct().ToArray();
            query = query.Where(x => ids.Contains(x.Id));
        }
        var growers = await query.OrderBy(x => x.Id).Select(x => new { x.Id, x.Mobile }).ToListAsync();
        if (growers.Count == 0)
            return Conflict(new { message = "No active growers with a valid mobile number matched this selection." });

        var batch = Guid.NewGuid().ToString("N");
        var queued = 0;
        // Intentionally sequential: SmsService uses the request-scoped DbContext, which must never
        // be used concurrently. Each write is idempotent and delivery continues in the background.
        foreach (var grower in growers)
        {
            if (await _sms.QueueRawAsync("MANUAL_FARMER_BROADCAST", grower.Id, null, grower.Mobile,
                    $"BCAST-{batch}-{grower.Id}", message))
                queued++;
        }
        await _audit.LogAsync("QueueSmsBroadcast", "Sms", "SmsBroadcast", batch,
            newValue: new { TargetMode = req.SendToAll ? "ALL_ACTIVE" : "SELECTED", TargetCount = growers.Count, Queued = queued, MessageLength = message.Length });
        return Ok(new { message = $"{queued} SMS queued for delivery. Check SMS Logs for SENT or FAILED status.",
            targetCount = growers.Count, queued });
    }
}

public class SmsBroadcastRequest
{
    public bool SendToAll { get; set; }
    public List<int>? GrowerIds { get; set; }
    public string? Message { get; set; }
}
