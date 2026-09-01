using CaneFactory.API.Auth;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

/// <summary>
/// Centralized Print Engine API (Phase 7). ONE endpoint renders any purchase slip in either the
/// currently configured printer format (A4 PDF / DotMatrix ESC-P) or an explicit override, and in
/// either final (bytes to send to the printer) or preview (WYSIWYG) form. Payment/Loan/SalePurchase
/// printing will add sibling actions here later using the exact same IPrintEngineService.
/// </summary>
[ApiController]
[Authorize]
[Route("api/print")]
public class PrintController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAuditService _audit;
    private readonly IPrintEngineService _engine;

    public PrintController(AppDbContext db, ICurrentUser current, IAuditService audit, IPrintEngineService engine)
    {
        _db = db; _current = current; _audit = audit; _engine = engine;
    }

    /// <summary>Renders the Gross/Tare slip. format=final (default) is auditable Print/Reprint and is
    /// what Flutter sends to the printer; format=preview never touches the audit log or counters.</summary>
    [HttpGet("purchase/{id:int}")]
    public async Task<IActionResult> PurchaseSlip(int id, [FromQuery] string stage,
        [FromQuery] string? target, [FromQuery] string format = "final")
    {
        if (!_current.HasPermission("Weighment.Print"))
            return StatusCode(403, new { message = "You do not have 'Weighment.Print' permission." });

        stage = (stage ?? "").Trim().ToUpperInvariant();
        if (stage is not ("GROSS" or "TARE")) return BadRequest(new { message = "stage must be GROSS or TARE." });
        if (format is not ("final" or "preview")) return BadRequest(new { message = "format must be final or preview." });

        var cfg = await _db.PrintConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        var resolvedTarget = target ?? cfg?.PrinterType ?? "DotMatrix";
        if (resolvedTarget is not ("A4" or "DotMatrix")) return BadRequest(new { message = "target must be A4 or DotMatrix." });

        var p = await _db.Purchases.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return NotFound(new { message = $"Purchase {id} not found." });
        if (stage == "TARE" && p.GrossTareStatus == "GROSS_DONE")
            return Conflict(new { message = "Tare slip is only available after Tare has been saved." });

        PrintDocument doc;
        try
        {
            doc = stage == "GROSS"
                ? await _engine.BuildGrossSlipAsync(id, _current.Username ?? "")
                : await _engine.BuildTareSlipAsync(id, _current.Username ?? "");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }

        var (bytes, contentType, ext) = _engine.Render(doc, resolvedTarget, format == "preview");

        if (format == "final")
        {
            var isReprint = stage == "GROSS" ? p.GrossPrintCount > 0 : p.TarePrintCount > 0;
            if (stage == "GROSS") p.GrossPrintCount++; else p.TarePrintCount++;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(isReprint ? "Reprint" : "Print", "Weighment", "Purchase", id.ToString(),
                newValue: new { stage, target = resolvedTarget, printCount = stage == "GROSS" ? p.GrossPrintCount : p.TarePrintCount });
        }
        return File(bytes, contentType, $"Purchase-{id}-{stage}.{ext}");
    }

    /// <summary>Renders the Loan issuance slip (Phase 8). Same final/preview semantics as the
    /// purchase slip above; format=final increments Loan.PrintCount and audits Print/Reprint.</summary>
    [HttpGet("loan/{id:int}")]
    public async Task<IActionResult> LoanSlip(int id, [FromQuery] string? target, [FromQuery] string format = "final")
    {
        if (!_current.HasPermission("Loan.Print"))
            return StatusCode(403, new { message = "You do not have 'Loan.Print' permission." });
        if (format is not ("final" or "preview")) return BadRequest(new { message = "format must be final or preview." });

        var cfg = await _db.PrintConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        var resolvedTarget = target ?? cfg?.PrinterType ?? "DotMatrix";
        if (resolvedTarget is not ("A4" or "DotMatrix")) return BadRequest(new { message = "target must be A4 or DotMatrix." });

        var loan = await _db.Loans.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (loan == null) return NotFound(new { message = $"Loan {id} not found." });

        PrintDocument doc;
        try { doc = await _engine.BuildLoanSlipAsync(id, _current.Username ?? ""); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }

        var (bytes, contentType, ext) = _engine.Render(doc, resolvedTarget, format == "preview");
        if (format == "final")
        {
            var isReprint = loan.PrintCount > 0;
            loan.PrintCount++;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(isReprint ? "Reprint" : "Print", "Loan", "Loan", id.ToString(),
                newValue: new { target = resolvedTarget, printCount = loan.PrintCount });
        }
        return File(bytes, contentType, $"Loan-{id}.{ext}");
    }

    /// <summary>Renders the Loan recovery receipt (Phase 8). format=final increments
    /// LoanRecovery.PrintCount and audits Print/Reprint.</summary>
    [HttpGet("loan-recovery/{id:int}")]
    public async Task<IActionResult> LoanRecoverySlip(int id, [FromQuery] string? target, [FromQuery] string format = "final")
    {
        if (!_current.HasPermission("LoanRecovery.Print"))
            return StatusCode(403, new { message = "You do not have 'LoanRecovery.Print' permission." });
        if (format is not ("final" or "preview")) return BadRequest(new { message = "format must be final or preview." });

        var cfg = await _db.PrintConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        var resolvedTarget = target ?? cfg?.PrinterType ?? "DotMatrix";
        if (resolvedTarget is not ("A4" or "DotMatrix")) return BadRequest(new { message = "target must be A4 or DotMatrix." });

        var recovery = await _db.LoanRecoveries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (recovery == null) return NotFound(new { message = $"Loan Recovery {id} not found." });

        PrintDocument doc;
        try { doc = await _engine.BuildLoanRecoverySlipAsync(id, _current.Username ?? ""); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }

        var (bytes, contentType, ext) = _engine.Render(doc, resolvedTarget, format == "preview");
        if (format == "final")
        {
            var isReprint = recovery.PrintCount > 0;
            recovery.PrintCount++;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(isReprint ? "Reprint" : "Print", "LoanRecovery", "LoanRecovery", id.ToString(),
                newValue: new { target = resolvedTarget, printCount = recovery.PrintCount });
        }
        return File(bytes, contentType, $"LoanRecovery-{id}.{ext}");
    }

    /// <summary>Renders the Payment slip (Phase 9). format=final increments Payment.PrintCount
    /// and audits Print/Reprint.</summary>
    [HttpGet("payment/{id:int}")]
    public async Task<IActionResult> PaymentSlip(int id, [FromQuery] string? target, [FromQuery] string format = "final")
    {
        if (!_current.HasPermission("Payment.Print"))
            return StatusCode(403, new { message = "You do not have 'Payment.Print' permission." });
        if (format is not ("final" or "preview")) return BadRequest(new { message = "format must be final or preview." });

        var cfg = await _db.PrintConfigs.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted);
        var resolvedTarget = target ?? cfg?.PrinterType ?? "DotMatrix";
        if (resolvedTarget is not ("A4" or "DotMatrix")) return BadRequest(new { message = "target must be A4 or DotMatrix." });

        var payment = await _db.Payments.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (payment == null) return NotFound(new { message = $"Payment {id} not found." });

        PrintDocument doc;
        try { doc = await _engine.BuildPaymentSlipAsync(id, _current.Username ?? ""); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }

        var (bytes, contentType, ext) = _engine.Render(doc, resolvedTarget, format == "preview");
        if (format == "final")
        {
            var isReprint = payment.PrintCount > 0;
            payment.PrintCount++;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(isReprint ? "Reprint" : "Print", "Payment", "Payment", id.ToString(),
                newValue: new { target = resolvedTarget, printCount = payment.PrintCount });
        }
        return File(bytes, contentType, $"Payment-{id}.{ext}");
    }

    /// <summary>Developer print test with sample data - no purchase/audit side effects. Lets the
    /// Developer verify printer, paper type, language, Hindi glyph rendering, QR readability and
    /// alignment before relying on Auto Print in production.</summary>
    [HasPermission("Print.Configure")]
    [HttpGet("test")]
    public IActionResult Test([FromQuery] string? target, [FromQuery] string? language, [FromQuery] string format = "final")
    {
        if (target is not (null or "A4" or "DotMatrix")) return BadRequest(new { message = "target must be A4 or DotMatrix." });
        if (language is not (null or "hi" or "en")) return BadRequest(new { message = "language must be hi or en." });
        if (format is not ("final" or "preview")) return BadRequest(new { message = "format must be final or preview." });

        var doc = _engine.BuildTestDocument(language ?? "hi", _current.Username ?? "Developer");
        var (bytes, contentType, ext) = _engine.Render(doc, target ?? "DotMatrix", format == "preview");
        return File(bytes, contentType, $"PrintTest.{ext}");
    }
}
