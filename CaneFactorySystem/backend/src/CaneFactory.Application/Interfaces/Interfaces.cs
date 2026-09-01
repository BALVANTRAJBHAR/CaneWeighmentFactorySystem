using CaneFactory.Application.DTOs;

namespace CaneFactory.Application.Interfaces;

public interface ICurrentUser
{
    int? UserId { get; }
    string? Username { get; }
    string? Role { get; }
    string? Ip { get; }
    string? Device { get; }
    bool HasPermission(string code);
}

public interface IAuditService
{
    Task LogAsync(string action, string module, string? entity = null, string? entityId = null,
        object? oldValue = null, object? newValue = null, bool success = true, string? failureReason = null);
}

public interface ISequenceGenerator
{
    /// <summary>Reserve the next business serial inside the CURRENT ambient transaction. Number is consumed only on commit.</summary>
    Task<long> NextAsync(string sequenceName, long startValue = 1);
}

public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string cipherText);
    string Hash(string value);
}

public interface ITokenService
{
    (string token, DateTime expiresAt) CreateAccessToken(int userId, string username, IEnumerable<string> roles, IEnumerable<string> permissions);
    string CreateRefreshTokenValue();
}

public interface IWeightFrameParser
{
    ParsedFrameDto Parse(byte[] frame);
}

public interface ILiveWeightBroadcaster
{
    Task BroadcastAsync(LiveWeightDto dto);
}
