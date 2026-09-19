using CaneFactory.API.Auth;
using CaneFactory.Application.Common;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

public class GrowerRequest
{
    public int VillageId { get; set; }
    public string GrowerName { get; set; } = string.Empty;
    public string? GrowerNameHi { get; set; }
    public string FatherName { get; set; } = string.Empty;
    public string? FatherNameHi { get; set; }
    public int? BankId { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? AccountHolderName { get; set; }
    public string? AadhaarNumber { get; set; }
    public string Mobile { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool Status { get; set; } = true;
    public bool AcceptDuplicateWarning { get; set; }
}

[ApiController]
[Authorize]
[Route("api/growers")]
public class GrowersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _current;
    private readonly ISequenceGenerator _seq;
    private readonly ISecretProtector _protector;
    private readonly ILogger<GrowersController> _log;

    public GrowersController(AppDbContext db, IAuditService audit, ICurrentUser current,
        ISequenceGenerator seq, ISecretProtector protector, ILogger<GrowersController> log)
    {
        _db = db; _audit = audit; _current = current; _seq = seq; _protector = protector; _log = log;
    }

    private IActionResult? Deny(string action) =>
        _current.HasPermission($"Grower.{action}") ? null
            : StatusCode(403, new { message = $"You do not have 'Grower.{action}' permission." });

    // Aadhaar is NEVER exposed in lists/reports - only masked last 4 for authorized viewers.
    private object ToDto(Grower g, string villageName, string? bankName) => new
    {
        g.Id, g.VillageId, VillageName = villageName, g.GrowerSequence, g.GrowerCode,
        g.GrowerName, g.GrowerNameHi, g.FatherName, g.FatherNameHi, g.BankId, BankName = bankName,
        AccountMasked = Mask(g.BankAccountNumber), g.AccountHolderName,
        AadhaarMasked = g.AadhaarLast4 == null ? null : $"XXXX-XXXX-{g.AadhaarLast4}",
        g.Mobile, g.Email, g.Status, g.CreatedAt
    };

    private static string? Mask(string? acc) =>
        string.IsNullOrEmpty(acc) ? null : (acc.Length <= 4 ? acc : new string('X', acc.Length - 4) + acc[^4..]);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string searchBy = "name",
        [FromQuery] bool includeInactive = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (Deny("View") is { } d) return d;
        var q = _db.Growers.Include(g => g.Village).Include(g => g.Bank).Where(g => !g.IsDeleted);
        if (!includeInactive) q = q.Where(g => g.Status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = searchBy switch
            {
                "father" => q.Where(g => g.FatherName.Contains(s)),
                "village" => q.Where(g => g.Village.VillageName.Contains(s)),
                "code" => q.Where(g => g.GrowerCode.Contains(s)),
                "mobile" => q.Where(g => g.Mobile.Contains(s)),
                _ => q.Where(g => g.GrowerName.Contains(s) || g.FatherName.Contains(s) || g.Village.VillageName.Contains(s))
            };
        }
        var total = await q.CountAsync();
        var items = (await q.OrderBy(g => g.GrowerCode).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync())
            .Select(g => ToDto(g, g.Village.VillageName, g.Bank?.BankName));
        return Ok(new { items, totalCount = total, page, pageSize });
    }

    [HttpGet("by-code")]
    public async Task<IActionResult> ByCode([FromQuery] string code)
    {
        if (Deny("View") is { } d) return d;
        var g = await _db.Growers.Include(x => x.Village).Include(x => x.Bank)
            .FirstOrDefaultAsync(x => x.GrowerCode == code.Trim() && !x.IsDeleted);
        if (g == null) return NotFound(new { message = $"No grower found with code '{code}'. Example: 101/1" });
        if (!g.Status) return Conflict(new { message = $"Grower '{g.GrowerName}' ({g.GrowerCode}) is INACTIVE." });
        return Ok(new GrowerLookupDto
        {
            GrowerId = g.Id, GrowerCode = g.GrowerCode, GrowerName = g.GrowerName, FatherName = g.FatherName,
            VillageId = g.VillageId, VillageName = g.Village.VillageName,
            BankName = g.Bank?.BankName, AccountMasked = Mask(g.BankAccountNumber), Mobile = g.Mobile
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (Deny("View") is { } d) return d;
        var g = await _db.Growers.Include(x => x.Village).Include(x => x.Bank)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        return g == null ? NotFound(new { message = "Grower not found." }) : Ok(ToDto(g, g.Village.VillageName, g.Bank?.BankName));
    }

    [HttpPost]
    public async Task<IActionResult> Create(GrowerRequest req)
    {
        if (Deny("Create") is { } d) return d;
        var error = await ValidateAsync(req, null);
        if (error != null) return Conflict(new { message = error });

        var warnings = await SoftWarningsAsync(req, null);
        if (warnings.Count > 0 && !req.AcceptDuplicateWarning)
            return UnprocessableEntity(new { message = "Possible duplicate grower.", warnings, requiresConfirmation = true });

        // Transaction-safe per-village GrowerSequence => GrowerCode = VillageId/Sequence
        Grower? g = null;
        try
        {
            await _db.ExecuteInTransactionAsync(async () =>
            {
                var growerId = (int)await _seq.NextAsync("GrowerId", 1);
                var sequence = (int)await _seq.NextAsync($"GrowerSeq:{req.VillageId}", 1);
                g = new Grower
                {
                    Id = growerId,
                    VillageId = req.VillageId,
                    GrowerSequence = sequence,
                    GrowerCode = $"{req.VillageId}/{sequence}",
                    GrowerName = Validators.Norm(req.GrowerName),
                    GrowerNameHi = string.IsNullOrWhiteSpace(req.GrowerNameHi) ? null : req.GrowerNameHi.Trim(),
                    FatherName = Validators.Norm(req.FatherName),
                    FatherNameHi = string.IsNullOrWhiteSpace(req.FatherNameHi) ? null : req.FatherNameHi.Trim(),
                    BankId = req.BankId,
                    BankAccountNumber = Validators.Norm(req.BankAccountNumber),
                    AccountHolderName = Validators.Norm(req.AccountHolderName),
                    Mobile = req.Mobile,
                    Email = Validators.Norm(req.Email),
                    CreatedBy = _current.UserId
                };
                ApplyAadhaar(g, req.AadhaarNumber);
                _db.Growers.Add(g);
                await _db.SaveChangesAsync();
            });
        }
        catch (DbUpdateException ex)
        {
            // The client gets an actionable, safe message; the server retains the
            // full database exception and trace id for diagnosis.
            _log.LogError(ex, "Unable to create grower. Aadhaar supplied: {HasAadhaar}. Trace: {TraceId}",
                !string.IsNullOrWhiteSpace(req.AadhaarNumber), HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Grower could not be saved. Aadhaar is optional. Apply the latest server database update and try again.",
                reference = HttpContext.TraceIdentifier
            });
        }
        var createdGrower = g!;
        await _audit.LogAsync("Create", "Grower", "Grower", createdGrower.Id.ToString(),
            newValue: new { createdGrower.GrowerCode, createdGrower.GrowerName, createdGrower.VillageId });
        return Ok(new { message = $"Grower '{createdGrower.GrowerName}' account created successfully. Grower Code: {createdGrower.GrowerCode}", id = createdGrower.Id, growerCode = createdGrower.GrowerCode });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, GrowerRequest req)
    {
        if (Deny("Edit") is { } d) return d;
        var g = await _db.Growers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (g == null) return NotFound(new { message = "Grower not found." });
        var error = await ValidateAsync(req, id);
        if (error != null) return Conflict(new { message = error });

        var old = new { g.GrowerName, g.FatherName, g.Mobile, g.BankId, g.Status };
        g.GrowerName = Validators.Norm(req.GrowerName);
        g.GrowerNameHi = string.IsNullOrWhiteSpace(req.GrowerNameHi) ? null : req.GrowerNameHi.Trim();
        g.FatherName = Validators.Norm(req.FatherName);
        g.FatherNameHi = string.IsNullOrWhiteSpace(req.FatherNameHi) ? null : req.FatherNameHi.Trim();
        g.BankId = req.BankId;
        g.BankAccountNumber = Validators.Norm(req.BankAccountNumber);
        g.AccountHolderName = Validators.Norm(req.AccountHolderName);
        g.Mobile = req.Mobile;
        g.Email = Validators.Norm(req.Email);
        g.Status = req.Status;
        if (!string.IsNullOrWhiteSpace(req.AadhaarNumber) && !req.AadhaarNumber.Contains('X'))
            ApplyAadhaar(g, req.AadhaarNumber);
        g.UpdatedAt = DateTime.UtcNow;
        g.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "Grower", "Grower", id.ToString(), oldValue: old,
            newValue: new { g.GrowerName, g.FatherName, g.Mobile, g.BankId, g.Status });
        return Ok(new { message = $"Grower '{g.GrowerName}' ({g.GrowerCode}) updated successfully." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> SoftDelete(int id)
    {
        if (Deny("Delete") is { } d) return d;
        var g = await _db.Growers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (g == null) return NotFound(new { message = "Grower not found." });
        if (await _db.Purchases.AnyAsync(p => p.GrowerId == id))
            return Conflict(new { message = "This Grower has Purchase transactions. Deactivate instead of deleting." });
        g.IsDeleted = true; g.DeletedAt = DateTime.UtcNow; g.DeletedBy = _current.UserId; g.Status = false;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Grower", "Grower", id.ToString(), oldValue: new { g.GrowerCode, g.GrowerName });
        return Ok(new { message = $"Grower '{g.GrowerName}' ({g.GrowerCode}) deleted (soft delete)." });
    }

    private void ApplyAadhaar(Grower g, string? aadhaar)
    {
        var normalized = aadhaar?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            // Explicitly persist database NULL, never an empty value or a stale
            // encrypted/hash value.  This is important for optional Aadhaar.
            g.AadhaarEncrypted = null;
            g.AadhaarLast4 = null;
            g.AadhaarHash = null;
            return;
        }

        g.AadhaarEncrypted = _protector.Protect(normalized);
        g.AadhaarLast4 = normalized[^4..];
        g.AadhaarHash = _protector.Hash(normalized);
    }

    private async Task<string?> ValidateAsync(GrowerRequest req, int? id)
    {
        if (Validators.Norm(req.GrowerName).Length == 0) return "Grower Name is required.";
        if (Validators.Norm(req.FatherName).Length == 0) return "Father Name is required.";
        if (!await _db.Villages.AnyAsync(v => v.Id == req.VillageId && !v.IsDeleted && v.Status))
            return "Selected Village does not exist or is inactive.";
        if (!Validators.IsMobile(req.Mobile)) return "Mobile must be exactly 10 numeric digits. Example: 9876543210";
        if (!Validators.IsEmail(req.Email)) return "Email format is invalid. Example: farmer@gmail.com";
        if (req.BankId.HasValue && !await _db.Banks.AnyAsync(b => b.Id == req.BankId && !b.IsDeleted && b.Status))
            return "Selected Bank does not exist or is inactive.";
        if (!string.IsNullOrWhiteSpace(req.AadhaarNumber) && !req.AadhaarNumber.Contains('X'))
        {
            if (!Validators.IsAadhaar(req.AadhaarNumber)) return "Aadhaar must be exactly 12 numeric digits.";
            var hash = _protector.Hash(req.AadhaarNumber);
            if (await _db.Growers.AnyAsync(g => !g.IsDeleted && g.Id != id && g.AadhaarHash == hash))
                return "DUPLICATE BLOCKED: This Aadhaar number is already registered to another grower.";
        }
        return null;
    }

    private async Task<List<string>> SoftWarningsAsync(GrowerRequest req, int? id)
    {
        var warnings = new List<string>();
        if (await _db.Growers.AnyAsync(g => !g.IsDeleted && g.Id != id && g.Mobile == req.Mobile))
            warnings.Add($"Mobile '{req.Mobile}' is already registered to another grower.");
        var name = Validators.Norm(req.GrowerName).ToLower();
        var father = Validators.Norm(req.FatherName).ToLower();
        if (await _db.Growers.AnyAsync(g => !g.IsDeleted && g.Id != id && g.VillageId == req.VillageId
                && g.GrowerName.ToLower() == name && g.FatherName.ToLower() == father))
            warnings.Add("A grower with the same Name + Father Name already exists in this Village.");
        return warnings;
    }
}
