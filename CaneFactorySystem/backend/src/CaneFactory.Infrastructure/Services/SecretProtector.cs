using System.Security.Cryptography;
using System.Text;
using CaneFactory.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace CaneFactory.Infrastructure.Services;

/// <summary>AES-256-GCM encryption for stored secrets. Key from env CANE_ENCRYPTION_KEY (any passphrase, derived via SHA-256).</summary>
public class SecretProtector : ISecretProtector
{
    private readonly byte[] _key;

    public SecretProtector(IConfiguration config)
    {
        var passphrase = config["Security:EncryptionKey"]
            ?? throw new InvalidOperationException("Security:EncryptionKey (env CANE_ENCRYPTION_KEY) is not configured.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(passphrase));
    }

    public string Protect(string plainText)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(plainText);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        var result = new byte[12 + 16 + cipher.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, 12);
        Buffer.BlockCopy(tag, 0, result, 12, 16);
        Buffer.BlockCopy(cipher, 0, result, 28, cipher.Length);
        return Convert.ToBase64String(result);
    }

    public string Unprotect(string cipherText)
    {
        var data = Convert.FromBase64String(cipherText);
        var nonce = data[..12];
        var tag = data[12..28];
        var cipher = data[28..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    public string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
