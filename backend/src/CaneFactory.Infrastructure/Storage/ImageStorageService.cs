using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CaneFactory.Infrastructure.Storage;

/// <summary>
/// Hybrid evidence storage. Local paths remain ordinary absolute paths for backwards
/// compatibility; cloud objects use azureblob://container/blob-name locators. The Azure secret
/// never lives in SystemSettings: production should use App Service managed identity and
/// Storage:AzureBlob:ServiceUri, with a connection string accepted only as a deployment fallback.
/// </summary>
public sealed class ImageStorageService(
    AppDbContext db,
    IConfiguration configuration,
    IMemoryCache memoryCache,
    ILogger<ImageStorageService> logger) : IImageStorageService
{
    public const string LocalMode = "LOCAL";
    public const string CloudMode = "AZURE_BLOB";
    private const string CloudScheme = "azureblob://";
    private static readonly Regex ContainerPattern = new("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TokenCredential SharedManagedIdentityCredential =
        new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true
        });

    public bool IsCloudLocator(string? locator) =>
        locator?.StartsWith(CloudScheme, StringComparison.OrdinalIgnoreCase) == true;

    public async Task<string> GetModeAsync(CancellationToken ct = default)
    {
        // Hosting determines where evidence is physically writable. In a cloud
        // + factory-edge deployment the shared database can be edited by either
        // API, so a stale LOCAL row must never redirect App Service/Edge writes
        // to two unrelated C: drives. A server environment override therefore
        // takes precedence over the user-facing database setting.
        var serverMode = configuration["Storage:ImageMode"]?.Trim().ToUpperInvariant();
        if (serverMode is LocalMode or CloudMode) return serverMode;
        var value = await SettingAsync("ImageStorageMode", ct);
        return string.Equals(value, CloudMode, StringComparison.OrdinalIgnoreCase) ? CloudMode : LocalMode;
    }

    public async Task EnsureCloudReadyAsync(string container, CancellationToken ct = default)
    {
        ValidateContainer(container);
        var client = CreateServiceClient().GetBlobContainerClient(container);
        await client.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        _ = await client.GetPropertiesAsync(cancellationToken: ct);
    }

    public async Task<string> SaveAsync(string localFullPath, string cloudRelativePath, byte[] bytes,
        bool overwrite = true, CancellationToken ct = default)
    {
        if (bytes.Length == 0) throw new InvalidDataException("Cannot store an empty evidence image.");
        if (await GetModeAsync(ct) != CloudMode)
        {
            var fullPath = Path.GetFullPath(localFullPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
                ?? throw new InvalidDataException("The local evidence path has no parent folder."));
            await using var output = new FileStream(fullPath, overwrite ? FileMode.Create : FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await output.WriteAsync(bytes, ct);
            return fullPath;
        }

        var container = NormalizeContainer(await SettingAsync("AzureBlobContainer", ct));
        var prefix = NormalizeRelative(await SettingAsync("AzureBlobPrefix", ct) ?? string.Empty, allowEmpty: true);
        var relative = NormalizeRelative(cloudRelativePath, allowEmpty: false);
        var blobName = string.IsNullOrWhiteSpace(prefix) ? relative : $"{prefix}/{relative}";
        var contentType = Path.GetExtension(relative).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            _ => "image/jpeg"
        };
        var containerClient = CreateServiceClient().GetBlobContainerClient(container);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        var blob = containerClient.GetBlobClient(blobName);
        try
        {
            await blob.UploadAsync(BinaryData.FromBytes(bytes), new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Conditions = overwrite ? null : new BlobRequestConditions { IfNoneMatch = ETag.All }
            }, ct);
        }
        catch (RequestFailedException ex) when (!overwrite && ex.Status is 409 or 412)
        {
            throw new IOException("An evidence object with this name already exists.", ex);
        }
        return $"{CloudScheme}{container}/{blobName}";
    }

    public async Task<byte[]?> ReadAsync(string? locator, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(locator)) return null;
        if (!IsCloudLocator(locator))
            return File.Exists(locator) ? await File.ReadAllBytesAsync(locator, ct) : null;

        var (container, blobName) = ParseCloudLocator(locator);
        var cacheKey = $"evidence-image:{container}/{blobName}";
        if (memoryCache.TryGetValue<byte[]>(cacheKey, out var cached) && cached != null)
            return cached;
        try
        {
            var content = await CreateServiceClient().GetBlobContainerClient(container)
                .GetBlobClient(blobName).DownloadContentAsync(ct);
            var bytes = content.Value.Content.ToArray();
            memoryCache.Set(cacheKey, bytes, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
            });
            return bytes;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string? locator, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(locator)) return false;
        if (!IsCloudLocator(locator)) return File.Exists(locator);
        var (container, blobName) = ParseCloudLocator(locator);
        try
        {
            return (await CreateServiceClient().GetBlobContainerClient(container)
                .GetBlobClient(blobName).ExistsAsync(ct)).Value;
        }
        catch (RequestFailedException ex)
        {
            logger.LogWarning(ex, "Could not check Azure Blob evidence object {Locator}", locator);
            return false;
        }
    }

    private BlobServiceClient CreateServiceClient()
    {
        var connectionString = configuration["Storage:AzureBlob:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString)) return new BlobServiceClient(connectionString);

        var serviceUri = configuration["Storage:AzureBlob:ServiceUri"];
        if (!Uri.TryCreate(serviceUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                "Azure Blob storage is not configured. Set CANE_AZURE_BLOB_SERVICE_URI to the HTTPS blob service URL, or set the server-only CANE_AZURE_BLOB_CONNECTION_STRING.");
        return new BlobServiceClient(uri, SharedManagedIdentityCredential);
    }

    private async Task<string?> SettingAsync(string key, CancellationToken ct) =>
        await db.SystemSettings.AsNoTracking().Where(x => x.Key == key).Select(x => x.Value).FirstOrDefaultAsync(ct);

    public static string NormalizeContainer(string? value)
    {
        var container = (value ?? string.Empty).Trim().ToLowerInvariant();
        ValidateContainer(container);
        return container;
    }

    private static void ValidateContainer(string container)
    {
        if (!ContainerPattern.IsMatch(container) || container.Contains("--", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Azure Blob container must be 3-63 lowercase letters/numbers/hyphens, without consecutive hyphens.");
    }

    private static string NormalizeRelative(string value, bool allowEmpty)
    {
        var normalized = (value ?? string.Empty).Replace('\\', '/').Trim('/').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            if (allowEmpty) return string.Empty;
            throw new InvalidDataException("Cloud evidence path cannot be empty.");
        }
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(x => x is "." or ".." || x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("Cloud evidence path contains an invalid segment.");
        return string.Join('/', segments);
    }

    private static (string container, string blobName) ParseCloudLocator(string locator)
    {
        var remainder = locator[CloudScheme.Length..];
        var slash = remainder.IndexOf('/');
        if (slash <= 0 || slash == remainder.Length - 1)
            throw new InvalidDataException("Azure Blob evidence locator is invalid.");
        var container = NormalizeContainer(remainder[..slash]);
        var blobName = NormalizeRelative(remainder[(slash + 1)..], allowEmpty: false);
        return (container, blobName);
    }
}
