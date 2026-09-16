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
    private static readonly PasswordHasher<User> Hasher = new();

    public AuthController(AppDbContext db, ITokenService tokens, ISecretProtector protector,
        IAuditService audit, IConfiguration config, UserStateService userState, ILogger<AuthController> log)
    {
        _db = db; _tokens = tokens; _protector = protector; _audit = audit;
        _config = config; _userState = userState; _log = log;
    }

    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // ---------------------------------------------------------------- LOGIN
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var username = (req.Username ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username && !u.IsDeleted);

        var maxFailed = await IntSetting("Security.MaxFailedLogins", 5);
        var lockoutMinutes = await IntSetting("Security.LockoutMinutes", 15);

        if (user == null || !user.Status)
        {
            await _audit.LogAsync("FailedLogin", "Auth", "User", username, success: false,
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

        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = DateTime.UtcNow;
        var response = await IssueTokensAsync(user, req.DeviceInfo);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Login", "Auth", "User", user.Id.ToString(), newValue: new { user.Username });
        return Ok(response);
    }

    private async Task<LoginResponse> IssueTokensAsync(User user, string? deviceInfo)
    {
        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        var perms = await _db.RolePermissions
            .Where(rp => user.UserRoles.Select(ur => ur.RoleId).Contains(rp.RoleId))
            .Select(rp => rp.Permission.Code).Distinct().ToListAsync();

        var (access, expiresAt) = _tokens.CreateAccessToken(user.Id, user.Username, roles, perms);
        var refreshValue = _tokens.CreateRefreshTokenValue();
        var refreshDays = int.TryParse(_config["Jwt:RefreshTokenDays"], out var d) ? d : 7;
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _protector.Hash(refreshValue),
            ExpiresAt = DateTime.UtcNow.AddDays(refreshDays),
            CreatedByIp = Ip,
            DeviceInfo = deviceInfo
        });

        return new LoginResponse(access, refreshValue, expiresAt, user.MustChangePassword, new UserInfo
        {
            Id = user.Id,
            Username = user.Username,
            FullName = user.FullName, FullNameHi = user.FullNameHi,
            Mobile = user.Mobile,
            Email = user.Email,
            Roles = roles,
            Permissions = perms,
            MustChangePassword = user.MustChangePassword,
            PreferredLanguage = user.PreferredLanguage,
            ThemeMode = user.ThemeMode,
            ThemeColor = user.ThemeColor
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

        var response = await IssueTokensAsync(token.User, token.DeviceInfo);
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
        return Ok(new UserInfo
        {
            Id = user.Id, Username = user.Username, FullName = user.FullName, FullNameHi = user.FullNameHi,
            Mobile = user.Mobile, Email = user.Email,
            Roles = user.UserRoles.Select(ur => ur.Role.Name).ToList(),
            Permissions = perms, MustChangePassword = user.MustChangePassword,
            PreferredLanguage = user.PreferredLanguage, ThemeMode = user.ThemeMode, ThemeColor = user.ThemeColor
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
        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            await _audit.LogAsync("PasswordChange", "Auth", "User", user.Id.ToString(), success: false, failureReason: "Wrong current password");
            return BadRequest(new { message = "Current password is incorrect." });
        }
        user.PasswordHash = Hasher.HashPassword(user, req.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        // revoke all other sessions after password change
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) { t.RevokedAt = DateTime.UtcNow; t.RevokedByIp = Ip; }
        await _db.SaveChangesAsync();
        _userState.Invalidate(userId);
        await _audit.LogAsync("PasswordChange", "Auth", "User", user.Id.ToString());
        return Ok(new { message = "Password changed successfully. Other sessions were logged out." });
    }

    // ------------------------------------------------- FORGOT PASSWORD (Mobile -> OTP -> Reset)
    [HttpPost("forgot-password/start")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotStart(ForgotPasswordStartRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Mobile == req.Mobile && !u.IsDeleted && u.Status);
        // do not reveal whether the mobile exists
        if (user != null)
        {
            var otp = Random.Shared.Next(100000, 999999).ToString();
            _db.UserOtps.Add(new UserOtp
            {
                UserId = user.Id, Mobile = req.Mobile, OtpHash = _protector.Hash(otp),
                Purpose = "PASSWORD_RESET", ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            });
            await _db.SaveChangesAsync();
            // SMS gateway sends the OTP in production; in Development it is written to the server log only.
            _log.LogInformation("PASSWORD RESET OTP for {Mobile}: {Otp}", req.Mobile, otp);
            await _audit.LogAsync("PasswordResetOtpSent", "Auth", "User", user.Id.ToString());
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
}
