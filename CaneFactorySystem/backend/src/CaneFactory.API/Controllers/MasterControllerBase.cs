using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Common;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

/// <summary>
/// Base for all master CRUD controllers. Enforces server-side per-action permission checks
/// (View/Create/Edit/Delete), soft deletion, duplicate validation hooks, audit logging and
/// optional business-serial ID assignment through the transactional sequence generator.
/// </summary>
[ApiController]
[Authorize]
public abstract class MasterControllerBase<TEntity> : ControllerBase where TEntity : BaseEntity, new()
{
    protected readonly AppDbContext Db;
    protected readonly IAuditService Audit;
    protected readonly ICurrentUser Current;
    protected readonly ISequenceGenerator Sequences;

    protected MasterControllerBase(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator sequences)
    {
        Db = db; Audit = audit; Current = current; Sequences = sequences;
    }

    protected abstract string Module { get; }
    protected abstract IQueryable<TEntity> ApplySearch(IQueryable<TEntity> q, string search);
    protected abstract void ApplyUpdate(TEntity target, TEntity source);
    protected abstract string DisplayName(TEntity e);
    protected virtual Task<string?> ValidateAsync(TEntity e, int? existingId) => Task.FromResult<string?>(null);
    protected virtual bool UseSequenceId => false;
    protected virtual string SequenceName => typeof(TEntity).Name + "Id";
    protected virtual long SequenceStart => 1;
    protected virtual Task<object> ProjectListAsync(IQueryable<TEntity> q) =>
        Task.FromResult<object>(q.ToList());

    protected IActionResult? Deny(string action) =>
        Current.HasPermission($"{Module}.{action}") ? null
            : StatusCode(403, new { message = $"You do not have '{Module}.{action}' permission." });

    protected IQueryable<TEntity> BaseQuery(bool includeInactive) =>
        includeInactive ? Db.Set<TEntity>().Where(e => !e.IsDeleted)
                        : Db.Set<TEntity>().Where(e => !e.IsDeleted && e.Status);

    [HttpGet]
    public virtual async Task<IActionResult> List([FromQuery] string? search, [FromQuery] bool includeInactive = false,
        [FromQuery] string? sortBy = "id", [FromQuery] bool desc = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        if (Deny("View") is { } d) return d;
        var q = BaseQuery(includeInactive);
        if (!string.IsNullOrWhiteSpace(search)) q = ApplySearch(q, search.Trim());
        var total = await q.CountAsync();
        q = sortBy?.ToLower() == "name"
            ? (desc ? q.OrderByDescending(DisplayNameExpr()) : q.OrderBy(DisplayNameExpr()))
            : (desc ? q.OrderByDescending(e => e.Id) : q.OrderBy(e => e.Id));
        var items = await ProjectListAsync(q.Skip((page - 1) * pageSize).Take(pageSize));
        return Ok(new { items, totalCount = total, page, pageSize });
    }

    protected virtual System.Linq.Expressions.Expression<Func<TEntity, object>> DisplayNameExpr() => e => e.Id;

    [HttpGet("{id:int}")]
    public virtual async Task<IActionResult> Get(int id)
    {
        if (Deny("View") is { } d) return d;
        var e = await Db.Set<TEntity>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        return e == null ? NotFound(new { message = $"{Module} record not found." }) : Ok(e);
    }

    [HttpPost]
    public virtual async Task<IActionResult> Create([FromBody] TEntity entity)
    {
        if (Deny("Create") is { } d) return d;
        Sanitize(entity);
        var error = await ValidateAsync(entity, null);
        if (error != null) return Conflict(new { message = error });

        entity.CreatedAt = DateTime.UtcNow;
        entity.CreatedBy = Current.UserId;
        entity.Status = true;

        if (UseSequenceId)
        {
            await Db.ExecuteInTransactionAsync(async () =>
            {
                entity.Id = (int)await Sequences.NextAsync(SequenceName, SequenceStart);
                Db.Set<TEntity>().Add(entity);
                await Db.SaveChangesAsync();
            });
        }
        else
        {
            Db.Set<TEntity>().Add(entity);
            await Db.SaveChangesAsync();
        }
        await Audit.LogAsync("Create", Module, typeof(TEntity).Name, entity.Id.ToString(), newValue: entity);
        return Ok(new { message = $"{Module} '{DisplayName(entity)}' saved successfully.", id = entity.Id });
    }

    [HttpPut("{id:int}")]
    public virtual async Task<IActionResult> Update(int id, [FromBody] TEntity source)
    {
        if (Deny("Edit") is { } d) return d;
        var target = await Db.Set<TEntity>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (target == null) return NotFound(new { message = $"{Module} record not found." });
        Sanitize(source);
        var error = await ValidateAsync(source, id);
        if (error != null) return Conflict(new { message = error });

        var old = System.Text.Json.JsonSerializer.Serialize(target,
            new System.Text.Json.JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles });
        ApplyUpdate(target, source);
        target.Status = source.Status;
        target.UpdatedAt = DateTime.UtcNow;
        target.UpdatedBy = Current.UserId;
        await Db.SaveChangesAsync();
        await Audit.LogAsync("Edit", Module, typeof(TEntity).Name, id.ToString(), oldValue: old, newValue: source);
        return Ok(new { message = $"{Module} '{DisplayName(target)}' updated successfully." });
    }

    [HttpDelete("{id:int}")]
    public virtual async Task<IActionResult> SoftDelete(int id)
    {
        if (Deny("Delete") is { } d) return d;
        var e = await Db.Set<TEntity>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (e == null) return NotFound(new { message = $"{Module} record not found." });
        var inUse = await InUseReasonAsync(id);
        if (inUse != null) return Conflict(new { message = inUse });
        e.IsDeleted = true;
        e.DeletedAt = DateTime.UtcNow;
        e.DeletedBy = Current.UserId;
        e.Status = false;
        await Db.SaveChangesAsync();
        await Audit.LogAsync("Delete", Module, typeof(TEntity).Name, id.ToString(), oldValue: new { Name = DisplayName(e) });
        return Ok(new { message = $"{Module} '{DisplayName(e)}' deleted (soft delete - history preserved)." });
    }

    /// <summary>Override to block soft delete of business-critical referenced records.</summary>
    protected virtual Task<string?> InUseReasonAsync(int id) => Task.FromResult<string?>(null);

    private static void Sanitize(TEntity e)
    {
        e.CreatedBy = null;
        e.UpdatedAt = null;
        e.UpdatedBy = null;
        e.IsDeleted = false;
        e.DeletedAt = null;
        e.DeletedBy = null;
    }
}
