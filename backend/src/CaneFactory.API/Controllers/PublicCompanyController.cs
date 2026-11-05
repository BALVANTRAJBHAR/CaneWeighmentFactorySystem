using System.Net;
using System.Net.Mail;
using System.Text;
using CaneFactory.API.Services;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

public sealed class QuotationRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public string? Requirement { get; set; }
    public string? Company { get; set; }
    public string? ServiceType { get; set; }
    public string? Village { get; set; }
    public string? Position { get; set; }
    public string? ProductCategory { get; set; }
    public string? Website { get; set; }
    public IFormFile? Attachment { get; set; }
}

[ApiController]
[AllowAnonymous]
[Route("api/public/company")]
public class PublicCompanyController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PublicCompanyController> _log;

    public PublicCompanyController(AppDbContext db, ISecretProtector protector,
        IConfiguration configuration, ILogger<PublicCompanyController> log) =>
        (_db, _protector, _configuration, _log) = (db, protector, configuration, log);

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var c = await _db.CompanyConfigs.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted);
        if (c == null) return NotFound();
        return Ok(new { c.CompanyName, c.CompanyNameHi, c.Address, c.AddressHi,
            c.ContactMobile1, c.ContactMobile2, c.ContactEmail,
            c.InstagramUrl, c.FacebookUrl, c.TwitterUrl, c.YoutubeUrl, c.LinkedinUrl });
    }

    [HttpGet("logo")]
    public async Task<IActionResult> Logo()
    {
        var c = await _db.CompanyConfigs.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted);
        return c?.LogoData is { Length: > 0 }
            ? File(c.LogoData, c.LogoContentType ?? "image/png")
            : NotFound();
    }

    [HttpPost("quotation")]
    [EnableRateLimiting("quotation")]
    [RequestSizeLimit(1_300_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_300_000)]
    public async Task<IActionResult> Quote([FromForm] QuotationRequest req, CancellationToken ct)
    {
        // Honeypot, server validation and per-IP limits protect this anonymous route.
        if (!string.IsNullOrWhiteSpace(req.Website)) return Ok(new { message = "Request received." });
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Farmer & Sugarcane", "B2B Sugar Supply", "Vendor & Supplier",
            "Careers", "Clean Energy", "General Inquiry", "Formal Quotation",
            "Developer / Software"
        };
        var category = allowed.FirstOrDefault(x => string.Equals(x,
            req.ServiceType?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (category == null || !Valid(req.Name, 120, true) ||
            !Valid(req.Mobile, 10, true) ||
            !System.Text.RegularExpressions.Regex.IsMatch(req.Mobile!.Trim(), @"^[6-9][0-9]{9}$") ||
            !Valid(req.Requirement, 3000, true) || !Valid(req.Company, 160) ||
            !Valid(req.Email, 200) || !Valid(req.Village, 120) ||
            !Valid(req.Position, 120) || !Valid(req.ProductCategory, 120) ||
            (category == "Farmer & Sugarcane" && string.IsNullOrWhiteSpace(req.Village)) ||
            (category == "Careers" && string.IsNullOrWhiteSpace(req.Position)) ||
            (category is "B2B Sugar Supply" or "Formal Quotation" &&
                (string.IsNullOrWhiteSpace(req.Company) || string.IsNullOrWhiteSpace(req.ProductCategory))) ||
            (category == "Vendor & Supplier" && string.IsNullOrWhiteSpace(req.Company)) ||
            (category is "B2B Sugar Supply" or "Careers" or "Formal Quotation" &&
                string.IsNullOrWhiteSpace(req.Email)) ||
            (!string.IsNullOrWhiteSpace(req.Email) && !MailAddress.TryCreate(req.Email.Trim(), out _)))
            return BadRequest(new { message = "Check required fields and enter a valid 10-digit mobile number." });

        byte[]? attachmentBytes = null;
        string? attachmentName = null;
        string? attachmentType = null;
        if (Request.Form.Files.Count > 0 && req.Attachment == null)
            return BadRequest(new { message = "Only the Attachment field may contain one file." });
        if (req.Attachment != null)
        {
            if (Request.Form.Files.Count != 1)
                return BadRequest(new { message = "Select only one attachment." });
            if (req.Attachment.Length is <= 0 or > 1_000_000)
                return BadRequest(new { message = "Attachment must be one PDF or JPG up to 1 MB." });
            var extension = Path.GetExtension(req.Attachment.FileName).ToLowerInvariant();
            if (extension is not (".pdf" or ".jpg" or ".jpeg"))
                return BadRequest(new { message = "Only PDF or JPG attachments are accepted." });
            await using var source = req.Attachment.OpenReadStream();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ct);
            attachmentBytes = buffer.ToArray();
            if (attachmentBytes.Length > 1_000_000)
                return BadRequest(new { message = "Attachment must be 1 MB or smaller." });
            var rawScan = await QuotationMalwareScanner.ScanAsync(attachmentBytes, _configuration, ct);
            if (rawScan is not ("OK" or "SKIPPED"))
                return StatusCode(503, new { message = rawScan == "INFECTED"
                    ? "Attachment rejected by the security scanner."
                    : "Attachment scanning is unavailable. Send without a file or try later." });
            if (extension == ".pdf")
            {
                if (attachmentBytes.Length < 8 || !attachmentBytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8) ||
                    !Encoding.Latin1.GetString(attachmentBytes).Contains("%%EOF", StringComparison.Ordinal))
                    return BadRequest(new { message = "Invalid PDF file." });
                var pdfText = Encoding.Latin1.GetString(attachmentBytes);
                if (new[] { "/JavaScript", "/JS", "/OpenAction", "/AA", "/Launch",
                            "/EmbeddedFile", "/RichMedia", "/XFA" }
                    .Any(x => pdfText.Contains(x, StringComparison.OrdinalIgnoreCase)))
                    return BadRequest(new { message = "PDF contains unsupported active content." });
                attachmentName = "attachment.pdf";
                attachmentType = "application/pdf";
            }
            else
            {
                if (attachmentBytes.Length < 4 || attachmentBytes[0] != 0xff || attachmentBytes[1] != 0xd8)
                    return BadRequest(new { message = "Invalid JPG image." });
                try
                {
                    using var bitmap = SkiaSharp.SKBitmap.Decode(attachmentBytes);
                    if (bitmap == null || bitmap.Width is < 1 or > 6000 || bitmap.Height is < 1 or > 6000 ||
                        (long)bitmap.Width * bitmap.Height > 20_000_000)
                        return BadRequest(new { message = "Invalid JPG image." });
                    using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                    using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 82);
                    attachmentBytes = encoded.ToArray(); // Strip metadata and compress.
                }
                catch { return BadRequest(new { message = "Invalid JPG image." }); }
                if (attachmentBytes.Length > 1_000_000)
                    return BadRequest(new { message = "Compressed JPG exceeds 1 MB. Select a smaller image." });
                attachmentName = "attachment.jpg";
                attachmentType = "image/jpeg";
            }
            // If scanning is enabled, a missing/unavailable scanner fails closed.
            var scan = await QuotationMalwareScanner.ScanAsync(attachmentBytes, _configuration, ct);
            if (scan is not ("OK" or "SKIPPED"))
                return StatusCode(503, new { message = scan == "INFECTED"
                    ? "Attachment rejected by the security scanner."
                    : "Attachment scanning is unavailable. Send without a file or try later." });
        }

        var c = await _db.CompanyConfigs.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted);
        var recipient = category == "Developer / Software" ? c?.DeveloperEmail : c?.ContactEmail;
        if (c == null || string.IsNullOrWhiteSpace(recipient) ||
            string.IsNullOrWhiteSpace(c.SmtpHost) || string.IsNullOrWhiteSpace(c.SmtpUsername) ||
            string.IsNullOrWhiteSpace(c.SmtpPasswordEncrypted) ||
            !MailAddress.TryCreate(recipient, out _))
            return StatusCode(503, new { message = "Quotation email is not configured yet. Please use Contact." });
        try
        {
            using var mail = new MailMessage(new MailAddress(c.SmtpUsername),
                new MailAddress(recipient))
            {
                Subject = $"Sugar mill inquiry: {category}",
                Body = $"Name: {req.Name?.Trim()}\nCompany: {req.Company?.Trim()}\nService: {category}\n" +
                    $"Email: {req.Email?.Trim()}\nMobile: {req.Mobile?.Trim()}\nVillage: {req.Village?.Trim()}\n" +
                    $"Position: {req.Position?.Trim()}\nProduct / service: {req.ProductCategory?.Trim()}\n\n" +
                    $"Requirement:\n{req.Requirement?.Trim()}"
            };
            if (!string.IsNullOrWhiteSpace(req.Email))
                mail.ReplyToList.Add(new MailAddress(req.Email.Trim()));
            if (attachmentBytes != null)
                mail.Attachments.Add(new Attachment(new MemoryStream(attachmentBytes), attachmentName!, attachmentType!));
            using var smtp = new SmtpClient(c.SmtpHost, c.SmtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(c.SmtpUsername,
                    _protector.Unprotect(c.SmtpPasswordEncrypted)),
                Timeout = 15000
            };
            await smtp.SendMailAsync(mail, ct);
            return Ok(new { message = "Your request was sent successfully." });
        }
        catch (Exception error)
        {
            _log.LogWarning(error, "Public quotation delivery failed");
            return StatusCode(503, new { message = "Could not send the request. Please try again or contact the factory." });
        }
    }

    private static bool Valid(string? value, int maximum, bool required = false) =>
        (!required || !string.IsNullOrWhiteSpace(value)) && (value?.Trim().Length ?? 0) <= maximum;
}
