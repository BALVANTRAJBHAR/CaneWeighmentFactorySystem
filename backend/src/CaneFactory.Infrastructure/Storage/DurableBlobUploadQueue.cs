using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CaneFactory.Infrastructure.Storage;

/// <summary>
/// Persists failed Azure evidence uploads before acknowledging the storage write.
/// Queue entries survive API/IIS restarts and are retried independently of the
/// weighment request that produced the image.
/// </summary>
public sealed class DurableBlobUploadQueue(
    IConfiguration configuration,
    ILogger<DurableBlobUploadQueue> logger) : BackgroundService
{
    private static readonly TokenCredential SharedManagedIdentityCredential =
        new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true
        });

    private readonly SemaphoreSlim _processGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private string QueueRoot => configuration["Storage:UploadQueuePath"] ??
        (OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "CaneFactory", "EvidenceUploadQueue")
            : "/var/lib/canefactory/evidence-upload-queue");

    public async Task EnqueueAsync(string container, string blobName, string contentType, byte[] bytes,
        bool overwrite)
    {
        Directory.CreateDirectory(QueueRoot);
        var id = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var payloadPath = Path.Combine(QueueRoot, $"{id}.bin");
        var metadataPath = Path.Combine(QueueRoot, $"{id}.json");
        var payloadTemp = $"{payloadPath}.tmp";
        var metadataTemp = $"{metadataPath}.tmp";
        var item = new PendingBlobUpload(id, container, blobName, contentType, overwrite, 0, DateTime.UtcNow);

        await File.WriteAllBytesAsync(payloadTemp, bytes, CancellationToken.None);
        File.Move(payloadTemp, payloadPath);
        await File.WriteAllTextAsync(metadataTemp, JsonSerializer.Serialize(item, _jsonOptions),
            CancellationToken.None);
        File.Move(metadataTemp, metadataPath);
        logger.LogWarning("Evidence upload queued for retry: {Container}/{BlobName}", container, blobName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Evidence upload queue processing failed; it will retry shortly.");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task ProcessPendingAsync(CancellationToken ct)
    {
        if (!Directory.Exists(QueueRoot) || !await _processGate.WaitAsync(0, ct)) return;
        try
        {
            foreach (var metadataPath in Directory.EnumerateFiles(QueueRoot, "*.json"))
            {
                ct.ThrowIfCancellationRequested();
                PendingBlobUpload? item;
                try
                {
                    item = JsonSerializer.Deserialize<PendingBlobUpload>(
                        await File.ReadAllTextAsync(metadataPath, ct), _jsonOptions);
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    logger.LogError(ex, "Could not read evidence upload queue item {Path}", metadataPath);
                    continue;
                }
                if (item == null || item.NextAttemptUtc > DateTime.UtcNow) continue;

                var payloadPath = Path.Combine(QueueRoot, $"{item.Id}.bin");
                if (!File.Exists(payloadPath))
                {
                    logger.LogError("Evidence upload queue payload is missing for {Path}", metadataPath);
                    continue;
                }

                try
                {
                    var bytes = await File.ReadAllBytesAsync(payloadPath, ct);
                    var container = CreateServiceClient().GetBlobContainerClient(item.Container);
                    await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
                    await container.GetBlobClient(item.BlobName).UploadAsync(BinaryData.FromBytes(bytes),
                        new BlobUploadOptions
                        {
                            HttpHeaders = new BlobHttpHeaders { ContentType = item.ContentType },
                            Conditions = item.Overwrite
                                ? null
                                : new Azure.Storage.Blobs.Models.BlobRequestConditions
                                {
                                    IfNoneMatch = Azure.ETag.All
                                }
                        }, ct);
                    File.Delete(payloadPath);
                    File.Delete(metadataPath);
                    logger.LogInformation("Queued evidence uploaded: {Container}/{BlobName}",
                        item.Container, item.BlobName);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Azure.RequestFailedException ex) when (!item.Overwrite && ex.Status is 409 or 412)
                {
                    // A previous attempt may have reached Azure even if its response was lost.
                    // Treat the immutable object's presence as successful idempotent delivery.
                    File.Delete(payloadPath);
                    File.Delete(metadataPath);
                    logger.LogInformation("Queued evidence already exists in Azure: {Container}/{BlobName}",
                        item.Container, item.BlobName);
                }
                catch (Exception ex)
                {
                    var attempts = item.Attempts + 1;
                    var delaySeconds = Math.Min(300, 5 * Math.Pow(2, Math.Min(attempts, 6)));
                    var updated = item with
                    {
                        Attempts = attempts,
                        NextAttemptUtc = DateTime.UtcNow.AddSeconds(delaySeconds)
                    };
                    var temp = $"{metadataPath}.tmp";
                    await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(updated, _jsonOptions),
                        CancellationToken.None);
                    File.Move(temp, metadataPath, overwrite: true);
                    logger.LogWarning(ex,
                        "Queued evidence upload attempt {Attempts} failed for {Container}/{BlobName}",
                        attempts, item.Container, item.BlobName);
                }
            }
        }
        finally
        {
            _processGate.Release();
        }
    }

    private BlobServiceClient CreateServiceClient()
    {
        var connectionString = configuration["Storage:AzureBlob:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new BlobServiceClient(connectionString, CreateBlobClientOptions());
        var serviceUri = configuration["Storage:AzureBlob:ServiceUri"];
        if (!Uri.TryCreate(serviceUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Azure Blob storage is not configured for queued uploads.");
        return new BlobServiceClient(uri, SharedManagedIdentityCredential, CreateBlobClientOptions());
    }

    private static BlobClientOptions CreateBlobClientOptions() => new()
    {
        Retry =
        {
            Mode = RetryMode.Exponential,
            NetworkTimeout = TimeSpan.FromSeconds(15),
            MaxRetries = 2,
            Delay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(5)
        }
    };

    private sealed record PendingBlobUpload(
        string Id,
        string Container,
        string BlobName,
        string ContentType,
        bool Overwrite,
        int Attempts,
        DateTime NextAttemptUtc);
}
