using CaneFactory.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using SkiaSharp;

var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
var checks = 0;

async Task ExpectRejected(byte[] bytes, string name, Type exceptionType)
{
    using var memory = new MemoryStream(bytes);
    var file = new FormFile(memory, 0, bytes.Length, "image", name);
    try
    {
        await SafeImageUpload.ValidateAsync(file, 2_000_000, 16_000_000, config, CancellationToken.None);
        throw new Exception($"Unsafe upload was accepted: {name}");
    }
    catch (Exception ex) when (ex.GetType() == exceptionType) { checks++; }
}

await ExpectRejected("not an image"u8.ToArray(), "evidence.jpg", typeof(InvalidDataException));
await ExpectRejected(new byte[] { 0xff, 0xd8, 0xff, 0x00 }, "evidence.png", typeof(InvalidDataException));
await ExpectRejected(new byte[] { 0xff, 0xd8, 0xff, 0x00 }, "evidence.jpg", typeof(InvalidDataException));
await ExpectRejected(new byte[] { 0xff, 0xd8, 0xff, 0x00 }, "evidence.exe", typeof(InvalidDataException));

using (var bitmap = new SKBitmap(2, 2))
using (var canvas = new SKCanvas(bitmap))
{
    canvas.Clear(SKColors.Green);
    using var encoded = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Jpeg, 85);
    var cleanImage = encoded.ToArray();
    using var memory = new MemoryStream(cleanImage);
    var file = new FormFile(memory, 0, cleanImage.Length, "image", "clean.jpg");
    var result = await SafeImageUpload.ValidateAsync(file, 2_000_000, 16_000_000, config, CancellationToken.None);
    if (result.ContentType != "image/jpeg" || result.Bytes.Length == 0)
        throw new Exception("A valid image must upload when optional scanning is off.");
    checks++;
}

// Exercise the ClamAV INSTREAM protocol without relying on a production scanner.
async Task<string> ScanWithTestServer(byte[] bytes, string reply)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var server = Task.Run(async () =>
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var prefix = new byte[10];
        await stream.ReadExactlyAsync(prefix);
        if (!prefix.AsSpan().SequenceEqual("nINSTREAM\n"u8)) throw new Exception("Scanner command mismatch.");
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes);
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
        var received = new byte[length];
        await stream.ReadExactlyAsync(received);
        await stream.ReadExactlyAsync(lengthBytes);
        if (!received.AsSpan().SequenceEqual(bytes) || lengthBytes.Any(x => x != 0))
            throw new Exception("Scanner did not receive the original complete file.");
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(reply + "\n"));
    });
    var scannerConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["QuotationScanner:Enabled"] = "true",
        ["QuotationScanner:Host"] = "127.0.0.1",
        ["QuotationScanner:Port"] = port.ToString()
    }).Build();
    try
    {
        var result = await QuotationMalwareScanner.ScanAsync(bytes, scannerConfig, CancellationToken.None);
        await server;
        return result;
    }
    finally { listener.Stop(); }
}

var eicar = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"u8.ToArray();
if (await ScanWithTestServer(eicar, "stream: Eicar-Test-Signature FOUND") != "INFECTED")
    throw new Exception("An infected scanner reply was not rejected.");
checks++;
if (await ScanWithTestServer("safe bytes"u8.ToArray(), "stream: OK") != "OK")
    throw new Exception("A clean scanner reply was not accepted.");
checks++;
if (await QuotationMalwareScanner.ScanAsync(eicar, config, CancellationToken.None) != "SKIPPED")
    throw new Exception("Disabled scanner must not block uploads.");
checks++;
var enabledWithoutHost = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["QuotationScanner:Enabled"] = "true"
}).Build();
if (await QuotationMalwareScanner.ScanAsync(eicar, enabledWithoutHost, CancellationToken.None) != "UNAVAILABLE")
    throw new Exception("Enabled scanner without a host must fail closed.");
checks++;
Console.WriteLine($"PASS: {checks} upload content, scanner-protocol and opt-in checks.");
