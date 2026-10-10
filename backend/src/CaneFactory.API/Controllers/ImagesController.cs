using System.Security.Cryptography;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Camera;
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
    private readonly IConfiguration? _configuration;
    public ImagesController(AppDbContext db, ICurrentUser current, IConfiguration? configuration = null)
    {
        _db = db; _current = current; _configuration = configuration;
    }

    /// <summary>
    /// Finds image evidence by the identifier selected in the View Images screen. IDs are exact;
    /// grower/party code or name searches can return the matching image list.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? type, [FromQuery] string? q)
    {
        var source = (type ?? string.Empty).Trim().ToLowerInvariant();
        var term = (q ?? string.Empty).Trim();
        if (source is not ("purchase" or "sale-purchase" or "payment" or "rate-edit"))
            return BadRequest(new { message = "Image type must be purchase, sale-purchase, payment, or rate-edit." });
        if (string.IsNullOrWhiteSpace(term))
            return BadRequest(new { message = "Enter a Purchase ID, Sale ID, Payment ID, Advice Number, Grower ID/Name, or Party Name." });

        return source switch
        {
            "purchase" => await SearchPurchaseImagesAsync(term),
            "sale-purchase" => await SearchSalePurchaseImagesAsync(term),
            "payment" => await SearchPaymentImagesAsync(term),
            "rate-edit" => await SearchRateEditImagesAsync(term),
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

    /// <summary>Serves only evidence consumed by a committed Cane/Sale rate change.</summary>
    [HttpGet("rate-edit/{id:int}/file")]
    public async Task<IActionResult> RateEditFile(int id)
    {
        if (DenyImageView() is { } denied) return denied;
        var evidence = await _db.RateOverrideEvidences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.Status && !x.IsDeleted &&
                _db.WeighmentRateOverrides.Any(o => o.EvidenceId == x.Id && o.Status && !o.IsDeleted));
        if (evidence == null) return NotFound(new { message = "Committed rate-update image was not found." });
        return await SendRateEditImageAsync(evidence.FilePath, evidence.ImageName, evidence.FileHash);
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
            query = query.Where(x => x.image.PurchaseId == purchaseId || x.purchase.GrowerId == purchaseId);
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
                growerId = x.purchase.GrowerId,
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
            query = query.Where(x => x.image.PaymentId == numericTerm || x.payment.AdviceNumber == numericTerm ||
                x.payment.GrowerId == numericTerm);
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
                growerId = x.payment.GrowerId,
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

    private async Task<IActionResult> SearchRateEditImagesAsync(string term)
    {
        if (DenyImageView() is { } denied) return denied;
        if (!int.TryParse(term, out var transactionId) || transactionId <= 0)
            return BadRequest(new { message = "Enter an exact Cane Purchase ID or Sale ID for rate-update images." });

        var items = await (
            from rateOverride in _db.WeighmentRateOverrides.AsNoTracking()
            join evidence in _db.RateOverrideEvidences.AsNoTracking()
                on rateOverride.EvidenceId equals evidence.Id
            where rateOverride.TransactionId == transactionId && rateOverride.Status && !rateOverride.IsDeleted &&
                evidence.Status && !evidence.IsDeleted
            orderby evidence.CreatedAt descending
            select new
            {
                imageId = evidence.Id,
                sourceType = "rate-edit",
                evidence.ImageName,
                evidence.Source,
                capturedAt = evidence.CreatedAt,
                rateOverride.TransactionType,
                rateOverride.TransactionId,
                rateOverride.MasterRate,
                rateOverride.ApprovedRate,
                rateOverride.ApprovedByUserName,
                rateOverride.RateReasonText,
                rateOverride.Remark
            }).Take(SearchLimit).ToListAsync();

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

    private async Task<IActionResult> SendRateEditImageAsync(string filePath, string imageName, string fileHash)
    {
        var (currentRoot, legacyRoot) = await ResolveRateEditRootsAsync();
        if (!RateEditImagePathResolver.IsUnderRoot(filePath, currentRoot) &&
            !RateEditImagePathResolver.IsUnderRoot(filePath, legacyRoot))
            return Conflict(new { message = "Rate-update image path failed the security check." });
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { message = "Image metadata exists but the file is missing on disk." });
        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        byte[] expectedHash;
        try { expectedHash = Convert.FromHexString(fileHash); }
        catch (FormatException) { return Conflict(new { message = "Image integrity metadata is invalid." }); }
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), expectedHash))
            return Conflict(new { message = "Image integrity verification failed." });
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(bytes, "image/jpeg", imageName);
    }

    private async Task<(string CurrentRoot, string LegacyRoot)> ResolveRateEditRootsAsync()
    {
        var configured = _configuration?["Storage:ImageRoot"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = (await _db.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == "ImageStorageRoot"))?.Value;
        var currentRoot = RateEditImagePathResolver.ResolveRoot(configured);

        var paymentRootSetting = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == "PaymentEvidenceRoot");
        var paymentRoot = string.IsNullOrWhiteSpace(paymentRootSetting?.Value)
            ? WeighmentImagePathResolver.NormalizeConfiguredPath(@"C:\WeighmentImage\Payment")
            : WeighmentImagePathResolver.NormalizeConfiguredPath(paymentRootSetting.Value);
        return (currentRoot, Path.Combine(paymentRoot, "Rate Overrides"));
    }
}

