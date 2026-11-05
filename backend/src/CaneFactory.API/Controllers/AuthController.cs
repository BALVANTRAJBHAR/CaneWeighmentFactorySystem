using CaneFactory.API.Auth;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;

namespace CaneFactory.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly ISecretProtector _protector;
    private readonly IAuditService _audit;
    private readonly IConfiguration _config;
    private readonly UserStateService _userState;
    private readonly ILogger<AuthController> _log;
    private readonly IHostEnvironment _environment;
    private static readonly PasswordHasher<User> Hasher = new();

    public AuthController(AppDbContext db, ITokenService tokens, ISecretProtector protector,
        IAuditService audit, IConfiguration config, UserStateService userState, ILogger<AuthController> log,
        IHostEnvironment environment)
    {
        _db = db; _tokens = tokens; _protector = protector; _audit = audit;
        _config = config; _userState = userState; _log = log; _environment = environment;
    }

    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // ---------------------------------------------------------------- LOGIN
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var identifier = (req.Username ?? string.Empty).Trim();
        var normalizedIdentifier = identifier.ToLowerInvariant();
        var isMobileIdentifier = identifier.Length == 10 && identifier.All(char.IsDigit);
        if (isMobileIdentifier && !Request.IsHttps && !IsLoopbackRequest())
            return StatusCode(StatusCodes.Status426UpgradeRequired,
                new { message = "Farmer mobile-number login requires HTTPS. Configure an https:// API URL." });

        var users = _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Where(u => !u.IsDeleted);
        User? user;
        if (isMobileIdentifier)
        {
            var farmerMatches = await users.Where(u => u.Mobile == identifier && u.GrowerId != null &&
                    u.UserRoles.Any(ur => ur.Role.Name == "Farmer" && ur.Role.Status && !ur.Role.IsDeleted))
                .Take(2).ToListAsync();
            user = farmerMatches.Count == 1 ? farmerMatches[0] : null;
        }
        else
        {
            user = await users.FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedIdentifier
                && !u.UserRoles.Any(ur => ur.Role.Name == "Farmer"));
        }

        var maxFailed = await IntSetting("Security.MaxFailedLogins", 5);
        var lockoutMinutes = await IntSetting("Security.LockoutMinutes", 15);

        if (user == null || !user.Status)
        {
            await _audit.LogAsync("FailedLogin", "Auth", "User",
                isMobileIdentifier ? MaskMobile(identifier) : normalizedIdentifier, success: false,
                failureReason: user == null ? "Unknown user" : "Account inactive");
            return Unauthorized(new { message = "Invalid username or password." });
        }
        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
        {
            await _audit.LogAsync("FailedLogin", "Auth", "User", user.Id.ToString(), success: false, failureReason: "Locked out");
            return Unauthorized(new { message = $"Account locked. Try again after {user.LockoutEnd:HH:mm} UTC." });
        }

        var verify = Hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password ?? string.Empty);
        if (verify == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= maxFailed)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(lockoutMinutes);
                user.FailedLoginCount = 0;
            }
            await _db.SaveChangesAsync();
            await _audit.LogAsync("FailedLogin", "Auth", "User", user.Id.ToString(), success: false, failureReason: "Wrong password");
            return Unauthorized(new { message = "Invalid username or password." });
        }

        if (_config.GetValue<bool>("Security:StaffMfaEnabled") && RequiresMfa(user))
        {
            if (!Request.IsHttps && !IsLoopbackRequest())
                return StatusCode(426, new { message = "Secure HTTPS is required for staff login." });
            if (!ValidMailbox(user.Email))
                return StatusCode(503, new { message = "Your account has no valid email address for login verification. Ask an administrator to update it in Users & Roles." });
            var company = await _db.CompanyConfigs.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted);
            if (company == null ||
                string.IsNullOrWhiteSpace(company.SmtpHost) || string.IsNullOrWhiteSpace(company.SmtpUsername) ||
                string.IsNullOrWhiteSpace(company.SmtpPasswordEncrypted) || !ValidMailbox(company.SmtpUsername))
                return StatusCode(503, new { message = "Login verification email service is not configured. Contact the administrator." });
            var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var oldCodes = await _db.UserOtps.Where(x => x.UserId == user.Id && x.Purpose == "LOGIN_MFA" &&
                x.ConsumedAt == null).ToListAsync();
            foreach (var old in oldCodes) old.ConsumedAt = DateTime.UtcNow;
            var code = new UserOtp { UserId = user.Id, Mobile = user.Mobile,
                OtpHash = _protector.Hash(otp), Purpose = "LOGIN_MFA", ExpiresAt = DateTime.UtcNow.AddMinutes(5) };
            _db.UserOtps.Add(code);
            await _db.SaveChangesAsync();
            try
            {
                using var mail = new MailMessage(new MailAddress(company.SmtpUsername!), new MailAddress(user.Email!))
                { Subject = "Sugarcane Management System login code", Body = $"Your login code is {otp}. It expires in 5 minutes. If you did not request it, ignore this email." };
                using var smtp = new SmtpClient(company.SmtpHost, company.SmtpPort)
                { EnableSsl = true, Credentials = new NetworkCredential(company.SmtpUsername,
                    _protector.Unprotect(company.SmtpPasswordEncrypted)), Timeout = 10000 };
                await smtp.SendMailAsync(mail);
            }
            catch (Exception ex)
            {
                code.ConsumedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                _log.LogWarning(ex, "Staff MFA email delivery failed for user {UserId}", user.Id);
                return StatusCode(503, new { message = "Verification email could not be sent. Try again later." });
            }
            await _audit.LogAsync("LoginMfaCodeSent", "Auth", "User", user.Id.ToString());
            return Ok(new { mfaRequired = true, message = "Enter the 6-digit code sent to your registered email." });
        }

        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = DateTime.UtcNow;
        var response = await IssueTokensAsync(user, req.DeviceInfo);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Login", "Auth", "User", user.Id.ToString(), newValue: new { user.Username });
        return Ok(response);
    }

    [HttpPost("mfa/verify")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyMfa(MfaVerifyRequest req)
    {
        if (!_config.GetValue<bool>("Security:StaffMfaEnabled") ||
            (!Request.IsHttps && !IsLoopbackRequest()))
            return Unauthorized(new { message = "Login verification is unavailable." });
        if (req.Otp == null || req.Otp.Length != 6 || !req.Otp.All(char.IsAsciiDigit) ||
            req.Username == null || req.Username.Length > 100 ||
            req.Password == null || req.Password.Length > 512)
            return Unauthorized(new { message = "Invalid login verification." });
        var identifier = (req.Username ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _db.Users.Include(u => u.UserRoles).ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(u => !u.IsDeleted && u.Status && u.Username.ToLower() == identifier);
        if (user == null || !RequiresMfa(user) ||
            (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow))
            return Unauthorized(new { message = "Invalid login verification." });
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password ?? "") == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= await IntSetting("Security.MaxFailedLogins", 5))
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(await IntSetting("Security.LockoutMinutes", 15));
                user.FailedLoginCount = 0;
            }
            await _db.SaveChangesAsync();
            await _audit.LogAsync("LoginMfaFailed", "Auth", "User", user.Id.ToString(), success: false);
            return Unauthorized(new { message = "Invalid login verification." });
        }
        var code = await _db.UserOtps.Where(x => x.UserId == user.Id && x.Purpose == "LOGIN_MFA" &&
            x.ConsumedAt == null && x.ExpiresAt > DateTime.UtcNow && x.Attempts < 5)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
        var suppliedHash = _protector.Hash(req.Otp ?? "");
        if (code == null || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(code.OtpHash), Convert.FromHexString(suppliedHash)))
        {
            if (code != null) { code.Attempts++; await _db.SaveChangesAsync(); }
            await _audit.LogAsync("LoginMfaFailed", "Auth", "User", user.Id.ToString(), success: false);
            return Unauthorized(new { message = "Invalid or expired verification code." });
        }
        code.ConsumedAt = DateTime.UtcNow;
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = DateTime.UtcNow;
        var response = await IssueTokensAsync(user, req.DeviceInfo, mfaVerified: true);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("LoginMfaSuccess", "Auth", "User", user.Id.ToString());
        return Ok(response);
    }

    private static bool RequiresMfa(User user) => user.UserRoles.Any(x =>
        x.Role.Name is "Developer" or "Administrator" or "Admin" or "Accountant" or "SalePurchase");

    private static bool ValidMailbox(string? value)
    {
        var address = value?.Trim();
        return !string.IsNullOrWhiteSpace(address) && address.Length <= 254 &&
            address.IndexOfAny(['\r', '\n', ' ']) < 0 &&
            MailAddress.TryCreate(address, out var parsed) &&
            string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase) &&
            parsed.Host.Contains('.');
    }

    private async Task<LoginResponse> IssueTokensAsync(User user, string? deviceInfo, bool mfaVerified = false)
    {
        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        var isFarmer = roles.Contains("Farmer", StringComparer.OrdinalIgnoreCase);
        var perms = await _db.RolePermissions
            .Where(rp => user.UserRoles.Select(ur => ur.RoleId).Contains(rp.RoleId))
            .Select(rp => rp.Permission.Code).Distinct().ToListAsync();

        var (access, expiresAt) = _tokens.CreateAccessToken(user.Id, user.Username, user.TokenVersion, roles, perms, mfaVerified);
        var refreshValue = _tokens.CreateRefreshTokenValue();
        var refreshDays = int.TryParse(_config["Jwt:RefreshTokenDays"], out var d) ? d : 7;
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _protector.Hash(refreshValue),
            MfaVerified = mfaVerified,
            ExpiresAt = DateTime.UtcNow.AddDays(refreshDays),
            CreatedByIp = Ip,
            DeviceInfo = deviceInfo
        });

        return new LoginResponse(access, refreshValue, expiresAt, user.MustChangePassword, new UserInfo
        {
            Id = user.Id,
            Username = user.Username,
            FullName = user.FullName, FullNameHi = user.FullNameHi,
            Mobile = isFarmer ? MaskMobile(user.Mobile) : user.Mobile,
            Email = isFarmer ? MaskEmail(user.Email) : user.Email,
            Roles = roles,
            Permissions = perms,
            MustChangePassword = user.MustChangePassword,
            PreferredLanguage = user.PreferredLanguage,
            ThemeMode = user.ThemeMode,
            ThemeColor = user.ThemeColor,
            CanAuthorizeWeighmentCorrections = user.CanAuthorizeWeighmentCorrections
        });
    }

    // ------------------------------------------------------- REFRESH (rotation + reuse detection)
    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh(RefreshRequest req)
    {
        var hash = _protector.Hash(req.RefreshToken ?? string.Empty);
        var token = await _db.RefreshTokens.Include(t => t.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (token == null) return Unauthorized(new { message = "Invalid refresh token." });

        if (token.RevokedAt != null)
        {
            // Token reuse detected -> revoke the whole session family for this user.
            var active = await _db.RefreshTokens.Where(t => t.UserId == token.UserId && t.RevokedAt == null).ToListAsync();
            foreach (var t in active) { t.RevokedAt = DateTime.UtcNow; t.RevokedByIp = Ip; }
            await _db.SaveChangesAsync();
            await _audit.LogAsync("RefreshTokenReuse", "Auth", "User", token.UserId.ToString(), success: false,
                failureReason: "Revoked token reuse - all sessions revoked");
            return Unauthorized(new { message = "Session invalidated. Please login again." });
        }
        if (DateTime.UtcNow >= token.ExpiresAt) return Unauthorized(new { message = "Refresh token expired." });
        if (!token.User.Status || token.User.IsDeleted) return Unauthorized(new { message = "Account is inactive." });
        if (_config.GetValue<bool>("Security:StaffMfaEnabled") && RequiresMfa(token.User) && !token.MfaVerified)
            return Unauthorized(new { message = "This session needs login verification. Please login again." });

        var response = await IssueTokensAsync(token.User, token.DeviceInfo, token.MfaVerified);
        token.RevokedAt = DateTime.UtcNow;
        token.RevokedByIp = Ip;
        token.ReplacedByTokenHash = _protector.Hash(response.RefreshToken);
        await _db.SaveChangesAsync();
        return Ok(response);
    }

    // ---------------------------------------------------------------- LOGOUT
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest req)
    {
        var hash = _protector.Hash(req.RefreshToken ?? string.Empty);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null);
        if (token != null)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedByIp = Ip;
            await _db.SaveChangesAsync();
        }
        await _audit.LogAsync("Logout", "Auth");
        return Ok(new { message = "Logged out." });
    }

    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll()
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) { t.RevokedAt = DateTime.UtcNow; t.RevokedByIp = Ip; }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("LogoutAllSessions", "Auth", "User", userId.ToString());
        return Ok(new { message = $"All sessions logged out ({tokens.Count} revoked)." });
    }

    [Authorize]
    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions()
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var sessions = await _db.RefreshTokens.Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt).Take(50)
            .Select(t => new SessionDto
            {
                Id = t.Id, DeviceInfo = t.DeviceInfo, CreatedByIp = t.CreatedByIp,
                CreatedAt = t.CreatedAt, ExpiresAt = t.ExpiresAt,
                IsActive = t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow
            }).ToListAsync();
        return Ok(sessions);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted && u.Status);
        if (user == null) return Unauthorized(new { message = "Account is inactive." });
        var perms = await _db.RolePermissions
            .Where(rp => user.UserRoles.Select(ur => ur.RoleId).Contains(rp.RoleId))
            .Select(rp => rp.Permission.Code).Distinct().ToListAsync();
        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        var isFarmer = roles.Contains("Farmer", StringComparer.OrdinalIgnoreCase);
        return Ok(new UserInfo
        {
            Id = user.Id, Username = user.Username, FullName = user.FullName, FullNameHi = user.FullNameHi,
            Mobile = isFarmer ? MaskMobile(user.Mobile) : user.Mobile,
            Email = isFarmer ? MaskEmail(user.Email) : user.Email,
            Roles = roles,
            Permissions = perms, MustChangePassword = user.MustChangePassword,
            PreferredLanguage = user.PreferredLanguage, ThemeMode = user.ThemeMode, ThemeColor = user.ThemeColor,
            CanAuthorizeWeighmentCorrections = user.CanAuthorizeWeighmentCorrections
        });
    }

    // ------------------------------------------------- AUTHENTICATED PASSWORD CHANGE
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        if (req.NewPassword != req.ConfirmPassword)
            return BadRequest(new { message = "New password and confirmation do not match." });
        var strength = PasswordStrengthError(req.NewPassword);
        if (strength != null) return BadRequest(new { message = strength });

        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstAsync(u => u.Id == userId && !u.IsDeleted && u.Status);
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            await _audit.LogAsync("PasswordChange", "Auth", "User", user.Id.ToString(), success: false, failureReason: "Wrong current password");
            return BadRequest(new { message = "Current password is incorrect." });
        }
        user.PasswordHash = Hasher.HashPassword(user, req.NewPassword);
        user.MustChangePassword = false;
        user.TokenVersion++;
        user.UpdatedAt = DateTime.UtcNow;
        // Revoke every token minted with the old password/security stamp, then issue one fresh
        // rotated session for this device. Old access tokens fail their TokenVersion check.
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) { t.RevokedAt = DateTime.UtcNow; t.RevokedByIp = Ip; }
        var response = await IssueTokensAsync(user, Request.Headers.UserAgent.ToString(),
            User.FindFirst("mfa")?.Value == "1");
        await _db.SaveChangesAsync();
        _userState.Invalidate(userId);
        await _audit.LogAsync("PasswordChange", "Auth", "User", user.Id.ToString());
        return Ok(new
        {
            message = "Password changed successfully. Other sessions were logged out.",
            response.AccessToken,
            response.RefreshToken,
            response.AccessTokenExpiresAt,
            response.MustChangePassword,
            response.User
        });
    }

    // ------------------------------------------------- FORGOT PASSWORD (Mobile -> OTP -> Reset)
    [HttpPost("forgot-password/start")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotStart(ForgotPasswordStartRequest req)
    {
        var matches = await _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Where(u => u.Mobile == req.Mobile && !u.IsDeleted && u.Status).Take(3).ToListAsync();
        // A shared mobile is ambiguous: never guess which account should be reset.
        var user = matches.Count == 1 ? matches[0] : null;
        // do not reveal whether the mobile exists
        if (user != null)
        {
            var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            _db.UserOtps.Add(new UserOtp
            {
                UserId = user.Id, Mobile = req.Mobile, OtpHash = _protector.Hash(otp),
                Purpose = "PASSWORD_RESET", ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            });
            await _db.SaveChangesAsync();
            var delivered = false;
            var smsConfig = await _db.SmsConfigs.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted && x.Enabled);
            if (smsConfig != null && smsConfig.ProviderType != "ANDROID_SIM")
            {
                try
                {
                    var provider = HttpContext.RequestServices.GetRequiredService<ISmsProviderClient>();
                    var apiKey = smsConfig.ApiKeyEncrypted == null ? null : _protector.Unprotect(smsConfig.ApiKeyEncrypted);
                    var apiSecret = smsConfig.ApiSecretEncrypted == null ? null : _protector.Unprotect(smsConfig.ApiSecretEncrypted);
                    var (success, _, _) = await provider.SendAsync(smsConfig, apiKey, apiSecret, user.Mobile,
                        $"Your Sugarcane Management System password reset code is {otp}. It expires in 5 minutes.",
                        HttpContext.RequestAborted);
                    delivered = success;
                }
                catch (Exception ex) { _log.LogWarning(ex, "Password-reset SMS delivery failed for user {UserId}", user.Id); }
            }
            if (!delivered)
            {
                var company = await _db.CompanyConfigs.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted);
                if (company != null && !string.IsNullOrWhiteSpace(user.Email) &&
                    !string.IsNullOrWhiteSpace(company.SmtpHost) &&
                    !string.IsNullOrWhiteSpace(company.SmtpUsername) &&
                    !string.IsNullOrWhiteSpace(company.SmtpPasswordEncrypted))
                {
                    try
                    {
                        using var mail = new MailMessage(new MailAddress(company.SmtpUsername), new MailAddress(user.Email))
                        { Subject = "Sugarcane Management System password reset", Body = $"Your password reset code is {otp}. It expires in 5 minutes." };
                        using var smtp = new SmtpClient(company.SmtpHost, company.SmtpPort)
                        { EnableSsl = true, Credentials = new NetworkCredential(company.SmtpUsername,
                            _protector.Unprotect(company.SmtpPasswordEncrypted)), Timeout = 10000 };
                        await smtp.SendMailAsync(mail);
                        delivered = true;
                    }
                    catch (Exception ex) { _log.LogWarning(ex, "Password-reset email failed for user {UserId}", user.Id); }
                }
            }
            if (!delivered)
            {
                var pending = await _db.UserOtps.Where(x => x.UserId == user.Id && x.Purpose == "PASSWORD_RESET" &&
                    x.ConsumedAt == null).ToListAsync();
                foreach (var item in pending) item.ConsumedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await _audit.LogAsync("PasswordResetDeliveryUnavailable", "Auth", "User", user.Id.ToString(), success: false);
            }
            else await _audit.LogAsync("PasswordResetOtpSent", "Auth", "User", user.Id.ToString());
        }
        return Ok(new { message = "If the mobile number is registered, an OTP has been sent." });
    }

    [HttpPost("forgot-password/verify")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotVerify(ForgotPasswordVerifyRequest req)
    {
        var otp = await LatestValidOtpAsync(req.Mobile);
        if (otp == null || _protector.Hash(req.Otp) != otp.OtpHash)
        {
            if (otp != null) { otp.Attempts++; await _db.SaveChangesAsync(); }
            return BadRequest(new { message = "Invalid or expired OTP." });
        }
        return Ok(new { message = "OTP verified. You can now set a new password." });
    }

    [HttpPost("forgot-password/reset")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotReset(ForgotPasswordResetRequest req)
    {
        var strength = PasswordStrengthError(req.NewPassword);
        if (strength != null) return BadRequest(new { message = strength });
        var otp = await LatestValidOtpAsync(req.Mobile);
        if (otp == null || _protector.Hash(req.Otp) != otp.OtpHash)
            return BadRequest(new { message = "Invalid or expired OTP." });

        var user = await _db.Users.FirstAsync(u => u.Id == otp.UserId);
        user.PasswordHash = Hasher.HashPassword(user, req.NewPassword);
        user.MustChangePassword = false;
        user.TokenVersion++;
        otp.ConsumedAt = DateTime.UtcNow;
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) { t.RevokedAt = DateTime.UtcNow; t.RevokedByIp = Ip; }
        await _db.SaveChangesAsync();
        _userState.Invalidate(user.Id);
        await _audit.LogAsync("PasswordReset", "Auth", "User", user.Id.ToString());
        return Ok(new { message = "Password reset successfully. All old sessions were revoked. Please login." });
    }

    private async Task<UserOtp?> LatestValidOtpAsync(string mobile) =>
        await _db.UserOtps.Where(o => o.Mobile == mobile && o.Purpose == "PASSWORD_RESET"
                && o.ConsumedAt == null && o.ExpiresAt > DateTime.UtcNow && o.Attempts < 5)
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync();

    private static string? PasswordStrengthError(string pwd)
    {
        if (string.IsNullOrEmpty(pwd) || pwd.Length < 8) return "Password must be at least 8 characters.";
        if (!pwd.Any(char.IsUpper)) return "Password must contain an uppercase letter.";
        if (!pwd.Any(char.IsLower)) return "Password must contain a lowercase letter.";
        if (!pwd.Any(char.IsDigit)) return "Password must contain a digit.";
        return null;
    }

    private async Task<int> IntSetting(string key, int fallback)
    {
        var s = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
        return s != null && int.TryParse(s.Value, out var v) ? v : fallback;
    }

    private bool IsLoopbackRequest()
    {
        var remote = HttpContext.Connection.RemoteIpAddress;
        return remote != null && System.Net.IPAddress.IsLoopback(remote);
    }

    private static string MaskMobile(string mobile) =>
        mobile.Length < 4 ? "****" : $"XXXXXX{mobile[^4..]}";

    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.IndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }
}
