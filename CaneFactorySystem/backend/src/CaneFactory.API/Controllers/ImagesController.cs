using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

/// <summary>Serves captured evidence image bytes. Images are stored on disk only, never as DB BLOBs.</summary>
[ApiController]
[Authorize]
[Route("api/images")]
public class ImagesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    public ImagesController(AppDbContext db, ICurrentUser current) { _db = db; _current = current; }

    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> File(int id)
    {
        if (!_current.HasPermission("Image.View"))
            return StatusCode(403, new { message = "You do not have 'Image.View' permission." });
        var img = await _db.PurchaseImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.Status);
        if (img == null) return NotFound(new { message = "Image record not found." });
        if (!System.IO.File.Exists(img.FilePath))
            return NotFound(new { message = "Image metadata exists but the file is missing on disk." });
        var bytes = await System.IO.File.ReadAllBytesAsync(img.FilePath);
        return File(bytes, "image/jpeg", img.ImageName);
    }

    /// <summary>Serves Cash Evidence photos captured for a Payment (Phase 9).</summary>
    [HttpGet("payment/{id:int}/file")]
    public async Task<IActionResult> PaymentFile(int id)
    {
        if (!_current.HasPermission("CashEvidence.View"))
            return StatusCode(403, new { message = "You do not have 'CashEvidence.View' permission." });
        var img = await _db.PaymentImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.Status);
        if (img == null) return NotFound(new { message = "Image record not found." });
        if (!System.IO.File.Exists(img.FilePath))
            return NotFound(new { message = "Image metadata exists but the file is missing on disk." });
        var bytes = await System.IO.File.ReadAllBytesAsync(img.FilePath);
        return File(bytes, "image/jpeg", img.ImageName);
    }
}
