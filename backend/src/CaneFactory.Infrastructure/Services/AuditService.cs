using System.Text.Json;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;

namespace CaneFactory.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;

    public AuditService(AppDbContext db, ICurrentUser current)
    {
        _db = db;
        _current = current;
    }

    public async Task LogAsync(string action, string module, string? entity = null, string? entityId = null,
        object? oldValue = null, object? newValue = null, bool success = true, string? failureReason = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _current.UserId,
            Username = _current.Username,
            Role = _current.Role,
            Action = action,
            Module = module,
            Entity = entity,
            EntityId = entityId,
            OldValue = oldValue == null ? null : JsonSerializer.Serialize(oldValue),
            NewValue = newValue == null ? null : JsonSerializer.Serialize(newValue),
            Timestamp = DateTime.UtcNow,
            Ip = _current.Ip,
            Device = _current.Device,
            Success = success,
            FailureReason = failureReason
        });
        await _db.SaveChangesAsync();
    }
}
