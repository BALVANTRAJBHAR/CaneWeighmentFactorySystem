using System.Net;
using System.Net.Mail;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

public sealed record QuotationRequest(string Name, string Email, string Mobile,
    string Requirement, string? Company, string? ServiceType, string? Website);

[ApiController]
[AllowAnonymous]
[Route("api/public/company")]
public class PublicCompanyController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly ILogger<PublicCompanyController> _log;

    public PublicCompanyController(AppDbContext db, ISecretProtector protector,
        ILogger<PublicCompanyController> log) => (_db, _protector, _log) = (db, protector, log);

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
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Quote(QuotationRequest req)
    {
        // Hidden honeypot and tight bounds make the anonymous form inexpensive to reject.
        if (!string.IsNullOrWhiteSpace(req.Website)) return Ok(new { message = "Request received." });
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 120 ||
            string.IsNullOrWhiteSpace(req.Mobile) || req.Mobile.Length > 20 ||
            string.IsNullOrWhiteSpace(req.Requirement) || req.Requirement.Length > 3000 ||
            (req.Email?.Length ?? 0) > 200 || req.Company?.Length > 160 ||
            req.ServiceType?.Length > 80)
            return BadRequest(new { message = "Enter your name, mobile and requirement (up to 3000 characters)." });

        var c = await _db.CompanyConfigs.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted);
        if (c == null || string.IsNullOrWhiteSpace(c.ContactEmail) ||
            string.IsNullOrWhiteSpace(c.SmtpHost) || string.IsNullOrWhiteSpace(c.SmtpUsername) ||
            string.IsNullOrWhiteSpace(c.SmtpPasswordEncrypted))
            return StatusCode(503, new { message = "Quotation email is not configured yet. Please use Contact." });
        try
        {
            using var mail = new MailMessage(new MailAddress(c.SmtpUsername),
                new MailAddress(c.ContactEmail))
            {
                Subject = "Sugar mill quotation request",
                Body = $"Name: {req.Name}\nCompany: {req.Company}\nService: {req.ServiceType}\nEmail: {req.Email}\nMobile: {req.Mobile}\n\nRequirement:\n{req.Requirement}"
            };
            using var smtp = new SmtpClient(c.SmtpHost, c.SmtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(c.SmtpUsername,
                    _protector.Unprotect(c.SmtpPasswordEncrypted))
            };
            await smtp.SendMailAsync(mail);
            return Ok(new { message = "Quotation request sent to the factory." });
        }
        catch (Exception error)
        {
            _log.LogWarning(error, "Public quotation delivery failed");
            return StatusCode(503, new { message = "Could not send the request. Please try again or contact the factory." });
        }
    }
}
