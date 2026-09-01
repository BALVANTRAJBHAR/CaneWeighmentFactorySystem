namespace CaneFactory.Application.DTOs;

public record LoginRequest(string Username, string Password, string? DeviceInfo);
public record LoginResponse(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt,
    bool MustChangePassword, UserInfo User);
public record RefreshRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);
public record ForgotPasswordStartRequest(string Mobile);
public record ForgotPasswordVerifyRequest(string Mobile, string Otp);
public record ForgotPasswordResetRequest(string Mobile, string Otp, string NewPassword);

public class UserInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string? Email { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public bool MustChangePassword { get; set; }
    public string? PreferredLanguage { get; set; }
    public string? ThemeMode { get; set; }
    public string? ThemeColor { get; set; }
}

public class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string TemporaryPassword { get; set; } = string.Empty;
    public List<int> RoleIds { get; set; } = new();
}

public class UpdateUserRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool Status { get; set; } = true;
    public List<int> RoleIds { get; set; } = new();
}

public class SessionDto
{
    public int Id { get; set; }
    public string? DeviceInfo { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }
}
