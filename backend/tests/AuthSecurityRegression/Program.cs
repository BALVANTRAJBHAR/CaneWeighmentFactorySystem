using System.IdentityModel.Tokens.Jwt;
using CaneFactory.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Jwt:Secret"] = "regression-only-secret-unique-to-this-test",
    ["Jwt:AccessTokenMinutes"] = "30"
}).Build();
var service = new TokenService(config);
var first = service.CreateAccessToken(42, "test-staff", 1, new[] { "Admin" }, Array.Empty<string>());
var verified = service.CreateAccessToken(42, "test-staff", 1, new[] { "Admin" }, Array.Empty<string>(), true);
var reader = new JwtSecurityTokenHandler();
if (reader.ReadJwtToken(first.token).Claims.Single(x => x.Type == "mfa").Value != "0")
    throw new Exception("Ordinary sessions must not carry a verified MFA claim.");
if (reader.ReadJwtToken(verified.token).Claims.Single(x => x.Type == "mfa").Value != "1")
    throw new Exception("Verified sessions must carry the MFA claim.");
if ((first.expiresAt - DateTime.UtcNow).TotalMinutes is < 29 or > 31)
    throw new Exception("Configured 30-minute access-token duration was not used.");
Console.WriteLine("PASS: MFA token claims and configurable access-token lifetime.");
