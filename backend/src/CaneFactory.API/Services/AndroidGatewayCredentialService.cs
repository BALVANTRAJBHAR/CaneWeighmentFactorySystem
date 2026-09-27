using System.Security.Cryptography;

namespace CaneFactory.API.Services;

/// <summary>Hashes Android gateway API keys with PBKDF2. Plain keys are accepted only at the
/// registration/authentication boundary and are never persisted or logged.</summary>
public sealed class AndroidGatewayCredentialService
{
    private const int Iterations = 120_000;
    private const int HashLength = 32;

    public (string hash, string salt) Hash(string apiKey)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(apiKey, salt, Iterations, HashAlgorithmName.SHA256, HashLength);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public bool Verify(string apiKey, string hash, string salt)
    {
        try
        {
            var expected = Convert.FromBase64String(hash);
            var candidate = Rfc2898DeriveBytes.Pbkdf2(apiKey, Convert.FromBase64String(salt),
                Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(candidate, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
