using CaneFactory.Application.Common;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CaneFactory.API.Controllers;

/// <summary>Configurable Loan Type master (e.g. Fertilizer/Seed/Equipment/Emergency Loan).</summary>
[Route("api/loan-types")]
public class LoanTypesController : MasterControllerBase<LoanTypeMaster>
{
    public LoanTypesController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq)
        : base(db, audit, current, seq) { }

    protected override string Module => "LoanType";
    protected override string DisplayName(LoanTypeMaster e) => e.LoanTypeName;
    protected override System.Linq.Expressions.Expression<Func<LoanTypeMaster, object>> DisplayNameExpr() => e => e.LoanTypeName;
    protected override IQueryable<LoanTypeMaster> ApplySearch(IQueryable<LoanTypeMaster> q, string s) =>
        q.Where(t => t.LoanTypeName.Contains(s));

    protected override async Task<string?> ValidateAsync(LoanTypeMaster e, int? id)
    {
        e.LoanTypeName = Validators.Norm(e.LoanTypeName);
        if (e.LoanTypeName.Length == 0) return "Loan Type Name is required.";
        if (await Db.LoanTypes.AnyAsync(t => !t.IsDeleted && t.Id != id && t.LoanTypeName.ToLower() == e.LoanTypeName.ToLower()))
            return $"Loan Type '{e.LoanTypeName}' already exists.";
        return null;
    }

    protected override void ApplyUpdate(LoanTypeMaster t, LoanTypeMaster s)
    {
        t.LoanTypeName = s.LoanTypeName; t.Description = s.Description;
    }

    protected override async Task<string?> InUseReasonAsync(int id) =>
        await Db.Loans.AnyAsync(l => l.LoanTypeId == id)
            ? "This Loan Type is used in Loans and cannot be deleted." : null;
}

/// <summary>
/// Phase 8: Loan issuance and lifecycle. Loans are interest-free, manually recovered, with no
/// maximum amount limit. LoanId is a continuous business serial starting from 1 (SequenceGenerator),
/// never the EF auto-increment Id. Auto-deduction from Payments arrives in Phase 9.
/// </summary>
[ApiController]
[Authorize]
[Route("api/loans")]
public class LoanController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    private readonly ISequenceGenerator _seq;
    private readonly IMemoryCache _cache;

    public LoanController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq, IMemoryCache cache)
    {
        _db = db; _audit = audit; _current = current; _seq = seq; _cache = cache;
    }

    private IActionResult? Deny(string action) =>
        _current.HasPermission($"Loan.{action}") ? null
            : StatusCode(403, new { message = $"You do not have 'Loan.{action}' permission." });

    private bool IsFarmerOnly =>
        (_current.Role ?? "").Split(',').All(r => r is "Farmer" or "") && (_current.Role ?? "") != "";

    /// <summary>Farmer accounts see ONLY their own loans (object-ownership enforcement).</summary>
    private async Task<IQueryable<Loan>> ScopedQueryAsync()
    {
        var q = _db.Loans.Where(l => !l.IsDeleted);
        if (!IsFarmerOnly) return q;
        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == _current.UserId);
        return q.Where(l => l.Grower.Mobile == user.Mobile);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? growerCode, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (Deny("View") is { } d) return d;
        var q = await ScopedQueryAsync();
        if (!string.IsNullOrWhiteSpace(growerCode)) q = q.Where(l => l.GrowerCode == growerCode.Trim());
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(l => l.LoanStatus == status);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(l => l.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new
            {
                loanId = l.Id, l.GrowerCode, GrowerName = l.Grower.GrowerName, VillageName = l.Grower.Village.VillageName,
                LoanTypeName = l.LoanType.LoanTypeName, l.LoanAmount, l.RecoveredAmount, l.OutstandingAmount,
                l.IssueDate, l.IssuedByUserName, l.LoanStatus, l.PrintCount
            }).ToListAsync();
        return Ok(new { items, totalCount = total, page, pageSize });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (Deny("View") is { } d) return d;
        var q = await ScopedQueryAsync();
        var l = await q.Where(x => x.Id == id).Select(l => new
        {
            loanId = l.Id, l.GrowerCode, GrowerName = l.Grower.GrowerName, FatherName = l.Grower.FatherName,
            VillageName = l.Grower.Village.VillageName, LoanTypeName = l.LoanType.LoanTypeName,
            l.LoanAmount, l.RecoveredAmount, l.OutstandingAmount, l.IssueDate, l.IssuedByUserName,
            l.LoanStatus, l.Remarks, l.PrintCount
        }).FirstOrDefaultAsync();
        // 404 (not 403) for out-of-scope IDs: does not leak other farmers' record existence
        return l == null ? NotFound(new { message = "Loan not found." }) : Ok(l);
    }

    /// <summary>Outstanding loan summary for a Grower - groundwork for Phase 9 Payment auto-deduction.
    /// growerCode is a query parameter (not a path segment) because codes like "101/1" contain '/'
    /// which ASP.NET Core routing never decodes from a path segment.</summary>
    [HttpGet("outstanding")]
    public async Task<IActionResult> Outstanding([FromQuery] string growerCode)
    {
        if (Deny("View") is { } d) return d;
        if (string.IsNullOrWhiteSpace(growerCode)) return BadRequest(new { message = "growerCode is required. Example: 101/1" });
        var loans = await _db.Loans.Where(l => l.GrowerCode == growerCode.Trim() && l.LoanStatus == "ACTIVE" && !l.IsDeleted)
            .OrderBy(l => l.IssueDate)
            .Select(l => new { loanId = l.Id, LoanTypeName = l.LoanType.LoanTypeName, l.LoanAmount, l.RecoveredAmount, l.OutstandingAmount, l.IssueDate })
            .ToListAsync();
        return Ok(new { growerCode = growerCode.Trim(), totalOutstanding = loans.Sum(l => l.OutstandingAmount), loans });
    }

    [HttpPost]
    public async Task<IActionResult> Issue(LoanIssueRequest req)
    {
        if (Deny("Create") is { } d) return d;
        if (IsDuplicateRequest(req.IdempotencyKey, out var dup)) return dup!;

        var grower = await _db.Growers.Include(g => g.Village)
            .FirstOrDefaultAsync(g => g.GrowerCode == req.GrowerCode.Trim() && !g.IsDeleted);
        if (grower == null) return NotFound(new { message = $"No grower found with code '{req.GrowerCode}'. Example: 101/1" });
        if (!grower.Status) return Conflict(new { message = $"Grower '{grower.GrowerName}' is INACTIVE and cannot be used." });

        var loanType = await _db.LoanTypes.FirstOrDefaultAsync(t => t.Id == req.LoanTypeId && !t.IsDeleted && t.Status);
        if (loanType == null) return BadRequest(new { message = "Selected Loan Type does not exist or is inactive." });

        if (req.LoanAmount <= 0) return BadRequest(new { message = "Loan Amount must be greater than 0. Example: 5000.00" });

        var season = await _db.Seasons.FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted);
        if (season == null) return Conflict(new { message = "No ACTIVE season is configured. Ask Admin/Developer to activate a Season." });

        await using var tx = await _db.Database.BeginTransactionAsync();
        var loanId = (int)await _seq.NextAsync("LoanId", 1);
        var amount = WeightCalculator.R2(req.LoanAmount);
        var loan = new Loan
        {
            Id = loanId,
            GrowerId = grower.Id,
            GrowerCode = grower.GrowerCode,
            VillageId = grower.VillageId,
            LoanTypeId = loanType.Id,
            LoanAmount = amount,
            RecoveredAmount = 0,
            OutstandingAmount = amount,
            IssueDate = DateTime.UtcNow,
            IssuedByUserId = _current.UserId!.Value,
            IssuedByUserName = _current.Username ?? "",
            Remarks = req.Remarks,
            LoanStatus = "ACTIVE",
            SeasonId = season.Id,
            CreatedBy = _current.UserId
        };
        _db.Loans.Add(loan);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        await _audit.LogAsync("Issue", "Loan", "Loan", loanId.ToString(),
            newValue: new { loan.GrowerCode, loan.LoanAmount, LoanTypeName = loanType.LoanTypeName });
        return Ok(new
        {
            message = $"Loan issued successfully. Loan ID: {loanId}. Amount: Rs {amount:F2}.",
            loanId,
            loanAmount = amount,
            autoPrint = await AutoPrintAsync(loanId)
        });
    }

    /// <summary>Cancels a Loan before any recovery has been recorded - the transactional record is
    /// never hard-deleted, only its status changes; history stays visible in Audit.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] Dictionary<string, string> body)
    {
        if (Deny("Cancel") is { } d) return d;
        var reason = (body.GetValueOrDefault("reason") ?? "").Trim();
        if (reason.Length < 5) return BadRequest(new { message = "A cancellation reason (min 5 characters) is required." });
        var loan = await _db.Loans.FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted);
        if (loan == null) return NotFound(new { message = "Loan not found." });
        if (loan.LoanStatus != "ACTIVE") return Conflict(new { message = $"Loan {id} is already {loan.LoanStatus} and cannot be cancelled." });
        if (loan.RecoveredAmount > 0) return Conflict(new { message = "A Loan with recorded recoveries cannot be cancelled. Reverse the recoveries first." });
        var old = loan.LoanStatus;
        loan.LoanStatus = "CANCELLED";
        loan.OutstandingAmount = 0;
        loan.UpdatedAt = DateTime.UtcNow;
        loan.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Cancel", "Loan", "Loan", id.ToString(), oldValue: old,
            newValue: new { Status = "CANCELLED", Reason = reason });
        return Ok(new { message = $"Loan {id} cancelled. Reason recorded in audit log." });
    }

    private async Task<object?> AutoPrintAsync(int loanId)
    {
        var cfg = await _db.PrintConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        if (cfg == null || !cfg.AutoPrint || cfg.LoanCopies <= 0) return null;
        return new
        {
            printerType = cfg.PrinterType,
            printerName = cfg.PrinterName,
            copies = cfg.LoanCopies,
            language = cfg.Language,
            documentUrl = $"/api/print/loan/{loanId}?format=final"
        };
    }

    private bool IsDuplicateRequest(string? key, out IActionResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (_cache.TryGetValue($"idem:{key}", out _))
        {
            result = Conflict(new { message = "Duplicate request detected (idempotency key already processed)." });
            return true;
        }
        _cache.Set($"idem:{key}", true, TimeSpan.FromMinutes(10));
        return false;
    }
}

/// <summary>
/// Phase 8: manual Loan recovery entries. LRId is a continuous business serial starting from 1.
/// Every recovery is validated so it never exceeds the parent Loan's current outstanding balance.
/// </summary>
[ApiController]
[Authorize]
[Route("api/loan-recoveries")]
public class LoanRecoveryController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    private readonly ISequenceGenerator _seq;
    private readonly IMemoryCache _cache;

    public LoanRecoveryController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq, IMemoryCache cache)
    {
        _db = db; _audit = audit; _current = current; _seq = seq; _cache = cache;
    }

    private IActionResult? Deny(string action) =>
        _current.HasPermission($"LoanRecovery.{action}") ? null
            : StatusCode(403, new { message = $"You do not have 'LoanRecovery.{action}' permission." });

    private bool IsFarmerOnly =>
        (_current.Role ?? "").Split(',').All(r => r is "Farmer" or "") && (_current.Role ?? "") != "";

    /// <summary>Farmer accounts see ONLY recoveries on their own loans (object-ownership enforcement,
    /// mirrors LoanController.ScopedQueryAsync).</summary>
    private async Task<IQueryable<LoanRecovery>> ScopedQueryAsync()
    {
        var q = _db.LoanRecoveries.Where(r => !r.IsDeleted);
        if (!IsFarmerOnly) return q;
        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == _current.UserId);
        return q.Where(r => r.Loan.Grower.Mobile == user.Mobile);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? loanId, [FromQuery] string? growerCode,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (Deny("View") is { } d) return d;
        var q = await ScopedQueryAsync();
        if (loanId.HasValue) q = q.Where(r => r.LoanId == loanId);
        if (!string.IsNullOrWhiteSpace(growerCode)) q = q.Where(r => r.GrowerCode == growerCode.Trim());
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                recoveryId = r.Id, r.LoanId, r.GrowerCode, GrowerName = r.Loan.Grower.GrowerName,
                r.RecoveryAmount, r.RecoveryDate, r.RecoveredByUserName, r.RecoveryStatus, r.PrintCount
            }).ToListAsync();
        return Ok(new { items, totalCount = total, page, pageSize });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (Deny("View") is { } d) return d;
        var q = await ScopedQueryAsync();
        var r = await q.Where(x => x.Id == id).Select(r => new
        {
            recoveryId = r.Id, r.LoanId, r.GrowerCode, GrowerName = r.Loan.Grower.GrowerName,
            r.RecoveryAmount, r.RecoveryDate, r.RecoveredByUserName, r.Remarks, r.RecoveryStatus, r.PrintCount
        }).FirstOrDefaultAsync();
        // 404 (not 403) for out-of-scope IDs: does not leak other farmers' record existence
        return r == null ? NotFound(new { message = "Loan Recovery not found." }) : Ok(r);
    }

    [HttpPost]
    public async Task<IActionResult> Record(LoanRecoveryRequest req)
    {
        if (Deny("Create") is { } d) return d;
        if (IsDuplicateRequest(req.IdempotencyKey, out var dup)) return dup!;

        var loan = await _db.Loans.FirstOrDefaultAsync(l => l.Id == req.LoanId && !l.IsDeleted);
        if (loan == null) return NotFound(new { message = $"Loan ID {req.LoanId} does not exist." });
        if (loan.LoanStatus != "ACTIVE") return Conflict(new { message = $"Loan {req.LoanId} is {loan.LoanStatus} and cannot accept recoveries." });
        if (req.RecoveryAmount <= 0) return BadRequest(new { message = "Recovery Amount must be greater than 0." });
        var amount = WeightCalculator.R2(req.RecoveryAmount);
        if (amount > loan.OutstandingAmount)
            return Conflict(new { message = $"Recovery amount (Rs {amount:F2}) cannot exceed the outstanding balance (Rs {loan.OutstandingAmount:F2})." });

        await using var tx = await _db.Database.BeginTransactionAsync();
        var lrId = (int)await _seq.NextAsync("LRId", 1);
        var recovery = new LoanRecovery
        {
            Id = lrId,
            LoanId = loan.Id,
            GrowerId = loan.GrowerId,
            GrowerCode = loan.GrowerCode,
            RecoveryAmount = amount,
            RecoveryDate = DateTime.UtcNow,
            RecoveredByUserId = _current.UserId!.Value,
            RecoveredByUserName = _current.Username ?? "",
            Remarks = req.Remarks,
            RecoveryStatus = "ACTIVE",
            CreatedBy = _current.UserId
        };
        loan.RecoveredAmount = WeightCalculator.R2(loan.RecoveredAmount + amount);
        loan.OutstandingAmount = WeightCalculator.R2(loan.OutstandingAmount - amount);
        if (loan.OutstandingAmount == 0) loan.LoanStatus = "CLOSED";
        loan.UpdatedAt = DateTime.UtcNow;
        loan.UpdatedBy = _current.UserId;
        _db.LoanRecoveries.Add(recovery);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        await _audit.LogAsync("Recover", "LoanRecovery", "LoanRecovery", lrId.ToString(),
            newValue: new { recovery.LoanId, recovery.RecoveryAmount, loan.OutstandingAmount });
        return Ok(new
        {
            message = $"Recovery recorded successfully. Recovery ID: {lrId}. Remaining Outstanding: Rs {loan.OutstandingAmount:F2}.",
            recoveryId = lrId,
            recoveryAmount = amount,
            outstandingAmount = loan.OutstandingAmount,
            loanStatus = loan.LoanStatus,
            autoPrint = await AutoPrintAsync(lrId)
        });
    }

    /// <summary>Reverses an already-applied recovery - restores it to the Loan's outstanding balance
    /// and reopens a CLOSED loan if needed. Transactional record is never hard-deleted.</summary>
    [HttpPost("{id:int}/reverse")]
    public async Task<IActionResult> Reverse(int id, [FromBody] Dictionary<string, string> body)
    {
        if (Deny("Reverse") is { } d) return d;
        var reason = (body.GetValueOrDefault("reason") ?? "").Trim();
        if (reason.Length < 5) return BadRequest(new { message = "A reversal reason (min 5 characters) is required." });
        var recovery = await _db.LoanRecoveries.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (recovery == null) return NotFound(new { message = "Loan Recovery not found." });
        if (recovery.RecoveryStatus != "ACTIVE") return Conflict(new { message = $"Loan Recovery {id} is already reversed." });
        var loan = await _db.Loans.FirstOrDefaultAsync(l => l.Id == recovery.LoanId);
        if (loan == null) return Conflict(new { message = "Parent Loan record is missing." });

        recovery.RecoveryStatus = "REVERSED";
        recovery.UpdatedAt = DateTime.UtcNow;
        recovery.UpdatedBy = _current.UserId;
        loan.RecoveredAmount = WeightCalculator.R2(loan.RecoveredAmount - recovery.RecoveryAmount);
        loan.OutstandingAmount = WeightCalculator.R2(loan.OutstandingAmount + recovery.RecoveryAmount);
        if (loan.LoanStatus == "CLOSED") loan.LoanStatus = "ACTIVE";
        loan.UpdatedAt = DateTime.UtcNow;
        loan.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("Reverse", "LoanRecovery", "LoanRecovery", id.ToString(),
            oldValue: new { recovery.RecoveryAmount }, newValue: new { Reason = reason, loan.OutstandingAmount });
        return Ok(new { message = $"Loan Recovery {id} reversed. Reason recorded in audit log.", outstandingAmount = loan.OutstandingAmount });
    }

    private async Task<object?> AutoPrintAsync(int loanRecoveryId)
    {
        var cfg = await _db.PrintConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        if (cfg == null || !cfg.AutoPrint || cfg.LoanCopies <= 0) return null;
        return new
        {
            printerType = cfg.PrinterType,
            printerName = cfg.PrinterName,
            copies = cfg.LoanCopies,
            language = cfg.Language,
            documentUrl = $"/api/print/loan-recovery/{loanRecoveryId}?format=final"
        };
    }

    private bool IsDuplicateRequest(string? key, out IActionResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (_cache.TryGetValue($"idem:{key}", out _))
        {
            result = Conflict(new { message = "Duplicate request detected (idempotency key already processed)." });
            return true;
        }
        _cache.Set($"idem:{key}", true, TimeSpan.FromMinutes(10));
        return false;
    }
}
