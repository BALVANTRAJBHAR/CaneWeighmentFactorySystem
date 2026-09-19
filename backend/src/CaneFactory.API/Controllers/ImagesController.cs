using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

/// <summary>Searches and serves captured weighment/payment evidence images. Image bytes remain on disk, never in the database.</summary>
[ApiController]
[Authorize]
[Route("api/images")]
public class ImagesController : ControllerBase
{
    private const int SearchLimit = 100;

    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    public ImagesController(AppDbContext db, ICurrentUser current) { _db = db; _current = current; }

    /// <summary>
    /// Finds image evidence by the identifier selected in the View Images screen. IDs are exact;
    /// grower/party code or name searches can return the matching image list.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? type, [FromQuery] string? q)
    {
        var source = (type ?? string.Empty).Trim().ToLowerInvariant();
        var term = (q ?? string.Empty).Trim();
        if (source is not ("purchase" or "sale-purchase" or "payment"))
            return BadRequest(new { message = "Image type must be purchase, sale-purchase, or payment." });
        if (string.IsNullOrWhiteSpace(term))
            return BadRequest(new { message = "Enter a Purchase ID, Payment ID, Advice Number, Grower Code/Name, Sale ID, or Party Name." });

        return source switch
        {
            "purchase" => await SearchPurchaseImagesAsync(term),
            "sale-purchase" => await SearchSalePurchaseImagesAsync(term),
            "payment" => await SearchPaymentImagesAsync(term),
            _ => BadRequest()
        };
    }

    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> File(int id)
    {
        if (DenyImageView() is { } denied) return denied;
        var img = await _db.PurchaseImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.Status);
        return await SendImageAsync(img?.FilePath, img?.ImageName);
    }

    /// <summary>Serves cash evidence photos captured for a Payment.</summary>
    [HttpGet("payment/{id:int}/file")]
    public async Task<IActionResult> PaymentFile(int id)
    {
        if (DenyCashEvidenceView() is { } denied) return denied;
        var img = await _db.PaymentImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.Status);
        return await SendImageAsync(img?.FilePath, img?.ImageName);
    }

    /// <summary>Serves images captured during a standalone sale/purchase weighment.</summary>
    [HttpGet("sale-purchase/{id:int}/file")]
    public async Task<IActionResult> SalePurchaseFile(int id)
    {
        if (DenyImageView() is { } denied) return denied;
        var img = await _db.SalePurchaseImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.Status);
        return await SendImageAsync(img?.FilePath, img?.ImageName);
    }

    private async Task<IActionResult> SearchPurchaseImagesAsync(string term)
    {
        if (DenyImageView() is { } denied) return denied;
        var query =
            from image in _db.PurchaseImages.AsNoTracking()
            join purchase in _db.Purchases.AsNoTracking() on image.PurchaseId equals purchase.Id
            join grower in _db.Growers.AsNoTracking() on purchase.GrowerId equals grower.Id
            where image.Status && !purchase.IsDeleted
            select new { image, purchase, grower };

        if (int.TryParse(term, out var purchaseId) && purchaseId > 0)
            query = query.Where(x => x.image.PurchaseId == purchaseId);
        else
            query = query.Where(x => x.purchase.GrowerCode.Contains(term)
                || x.grower.GrowerCode.Contains(term)
                || x.grower.GrowerName.Contains(term));

        var items = await query
            .OrderByDescending(x => x.image.CapturedAt)
            .Take(SearchLimit)
            .Select(x => new
            {
                imageId = x.image.Id,
                sourceType = "purchase",
                x.image.ImageName,
                x.image.CameraId,
                x.image.CaptureStage,
                x.image.CapturedAt,
                purchaseId = x.image.PurchaseId,
                growerCode = x.purchase.GrowerCode,
                growerName = x.grower.GrowerName,
                fatherName = x.grower.FatherName,
                vehicleNumber = x.purchase.VehicleNumber,
                adviceNumber = x.purchase.AdviceNumber,
                transactionStatus = x.purchase.GrossTareStatus
            })
            .ToListAsync();

        return Ok(new { items, totalReturned = items.Count, cappedAt = SearchLimit });
    }

    private async Task<IActionResult> SearchSalePurchaseImagesAsync(string term)
    {
        if (DenyImageView() is { } denied) return denied;
        var query =
            from image in _db.SalePurchaseImages.AsNoTracking()
            join sale in _db.SalePurchases.AsNoTracking() on image.SalePurchaseId equals sale.Id
            join party in _db.Parties.AsNoTracking() on sale.PartyId equals party.Id
            where image.Status && !sale.IsDeleted
            select new { image, sale, party };

        if (int.TryParse(term, out var salePurchaseId) && salePurchaseId > 0)
            query = query.Where(x => x.image.SalePurchaseId == salePurchaseId);
        else
            query = query.Where(x => x.party.PartyName.Contains(term));

        var items = await query
            .OrderByDescending(x => x.image.CapturedAt)
            .Take(SearchLimit)
            .Select(x => new
            {
                imageId = x.image.Id,
                sourceType = "sale-purchase",
                x.image.ImageName,
                x.image.CameraId,
                x.image.CaptureStage,
                x.image.CapturedAt,
                salePurchaseId = x.image.SalePurchaseId,
                partyName = x.party.PartyName,
                vehicleNumber = x.sale.VehicleNumber,
                driverName = x.sale.DriverName,
                transactionStatus = x.sale.WeighmentStatus
            })
            .ToListAsync();

        return Ok(new { items, totalReturned = items.Count, cappedAt = SearchLimit });
    }

    private async Task<IActionResult> SearchPaymentImagesAsync(string term)
    {
        if (DenyCashEvidenceView() is { } denied) return denied;
        var query =
            from image in _db.PaymentImages.AsNoTracking()
            join payment in _db.Payments.AsNoTracking() on image.PaymentId equals payment.Id
            join grower in _db.Growers.AsNoTracking() on payment.GrowerId equals grower.Id
            join mode in _db.PaymentModes.AsNoTracking() on payment.PaymentModeId equals mode.Id
            where image.Status && !payment.IsDeleted
            select new { image, payment, grower, mode };

        if (int.TryParse(term, out var numericTerm) && numericTerm > 0)
            query = query.Where(x => x.image.PaymentId == numericTerm || x.payment.AdviceNumber == numericTerm);
        else
            query = query.Where(x => x.payment.GrowerCode.Contains(term)
                || x.grower.GrowerCode.Contains(term)
                || x.grower.GrowerName.Contains(term));

        var items = await query
            .OrderByDescending(x => x.image.CapturedAt)
            .Take(SearchLimit)
            .Select(x => new
            {
                imageId = x.image.Id,
                sourceType = "payment",
                x.image.ImageName,
                x.image.CameraId,
                x.image.CapturedAt,
                paymentId = x.image.PaymentId,
                purchaseId = x.image.PurchaseId,
                adviceNumber = x.payment.AdviceNumber,
                growerCode = x.payment.GrowerCode,
                growerName = x.grower.GrowerName,
                fatherName = x.grower.FatherName,
                paymentMode = x.mode.ModeName,
                amount = x.payment.NetPayableAmount,
                transactionStatus = x.payment.PaymentStatus
            })
            .ToListAsync();

        return Ok(new { items, totalReturned = items.Count, cappedAt = SearchLimit });
    }

    private IActionResult? DenyImageView() => _current.HasPermission("Image.View")
        ? null
        : StatusCode(StatusCodes.Status403Forbidden, new { message = "You do not have 'Image.View' permission." });

    private IActionResult? DenyCashEvidenceView() => _current.HasPermission("CashEvidence.View")
        ? null
        : StatusCode(StatusCodes.Status403Forbidden, new { message = "You do not have 'CashEvidence.View' permission." });

    private async Task<IActionResult> SendImageAsync(string? filePath, string? imageName)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(imageName))
            return NotFound(new { message = "Image record not found." });
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { message = "Image metadata exists but the file is missing on disk." });
        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(bytes, "image/jpeg", imageName);
    }
}

