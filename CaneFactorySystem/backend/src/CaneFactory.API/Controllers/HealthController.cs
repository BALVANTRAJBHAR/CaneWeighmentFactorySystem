using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace CaneFactory.API.Controllers;

/// <summary>
/// Phase 13: unauthenticated liveness/readiness probe for production monitoring (load balancer,
/// Task Scheduler watchdog, uptime pinger). Deliberately separate from the role-aware, permission
/// gated /api/dashboard/health used inside the app - this one must always be reachable with zero
/// credentials so infrastructure tooling can alert even when nobody is logged in.
/// </summary>
[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _db;
    public HealthController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        bool dbOk;
        try { dbOk = await _db.Database.CanConnectAsync(); } catch { dbOk = false; }
        var status = dbOk ? "healthy" : "degraded";
        return StatusCode(dbOk ? 200 : 503, new
        {
            status,
            service = "CaneFactory API",
            database = dbOk ? "connected" : "disconnected",
            serverTimeUtc = DateTime.UtcNow
        });
    }
}
