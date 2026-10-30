using System.Reflection;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Camera;
using CaneFactory.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

var provider = new DelayedSimulatorProvider();
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Camera:SimulatorMode"] = "true"
}).Build();
var service = new CameraCaptureService(null!, new PlainSecretProtector(), null!, configuration,
    new TestEnvironment(), new[] { provider }, null!, NullLogger<CameraCaptureService>.Instance);
var method = typeof(CameraCaptureService).GetMethod("CaptureCamerasAsync",
    BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Exception("Parallel capture method missing.");
var cameras = new List<CameraConfig>
{
    new() { Id = 1, CameraNumber = 1, Protocol = "RTSP" },
    new() { Id = 2, CameraNumber = 2, Protocol = "RTSP" }
};
var captures = (IAsyncEnumerable<(CameraConfig camera,
    (bool ok, byte[]? bytes, string? error) frame)>)method.Invoke(service,
    new object[] { cameras, CancellationToken.None })!;

var completionOrder = new List<int>();
await foreach (var capture in captures)
{
    if (!capture.frame.ok || capture.frame.bytes == null) throw new Exception("Capture unexpectedly failed.");
    completionOrder.Add(capture.camera.CameraNumber);
    if (completionOrder.Count == 1 && provider.Started != 2)
        throw new Exception("All camera captures were not started in parallel.");
}
if (!completionOrder.SequenceEqual(new[] { 2, 1 }))
    throw new Exception($"Expected completion order 2,1; got {string.Join(',', completionOrder)}.");

var queuePath = Path.Combine(Path.GetTempPath(), $"canefactory-camera-regression-{Guid.NewGuid():N}");
try
{
    var queueConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Storage:UploadQueuePath"] = queuePath
    }).Build();
    using var queue = new DurableBlobUploadQueue(queueConfiguration,
        NullLogger<DurableBlobUploadQueue>.Instance);
    await queue.EnqueueAsync("evidence", "test/frame.jpg", "image/jpeg", new byte[] { 4, 5, 6 }, true);
    if (Directory.GetFiles(queuePath, "*.json").Length != 1 ||
        Directory.GetFiles(queuePath, "*.bin").Length != 1)
        throw new Exception("Durable upload queue did not persist both metadata and image bytes.");
}
finally
{
    if (Directory.Exists(queuePath)) Directory.Delete(queuePath, recursive: true);
}

Console.WriteLine("Camera capture regression passed (3 checks).");

sealed class DelayedSimulatorProvider : ICameraCaptureProvider
{
    private int _started;
    public int Started => _started;
    public string Protocol => "SIMULATOR";

    public async Task<(bool success, byte[]? jpegBytes, string? error)> CaptureAsync(
        CameraConfig camera, string? plainPassword, CancellationToken ct)
    {
        Interlocked.Increment(ref _started);
        await Task.Delay(camera.CameraNumber == 1 ? 300 : 25, ct);
        return (true, new byte[] { 1, 2, 3 }, null);
    }
}

sealed class PlainSecretProtector : ISecretProtector
{
    public string Protect(string plainText) => plainText;
    public string Unprotect(string cipherText) => cipherText;
    public string Hash(string value) => value;
}

sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "CameraCaptureRegression";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
