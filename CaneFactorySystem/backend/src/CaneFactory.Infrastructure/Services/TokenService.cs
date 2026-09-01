using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CaneFactory.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CaneFactory.Infrastructure.Services;

public class TokenService : ITokenService
{
    private readonly IConfiguration _config;
    public TokenService(IConfiguration config) => _config = config;

    public (string token, DateTime expiresAt) CreateAccessToken(int userId, string username,
        IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var secret = _config["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret (env CANE_JWT_SECRET) is not configured.");
        var minutes = int.TryParse(_config["Jwt:AccessTokenMinutes"], out var m) ? m : 15;
        var expiresAt = DateTime.UtcNow.AddMinutes(minutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim("perm", p)));

        var key = new SymmetricSecurityKey(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "CaneFactory",
            audience: _config["Jwt:Audience"] ?? "CaneFactoryClients",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public string CreateRefreshTokenValue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}
