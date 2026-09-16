using System.Diagnostics;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CaneFactory.Infrastructure.Camera;

/// <summary>
/// Vendor-abstracted camera capture providers (Phase 6). One implementation per wire protocol;
/// the actual vendor (Hikvision/CPPlus/Dahua/Uniview/Generic) only changes the URL pattern, never
/// the code path. A SIMULATOR provider is used when no physical camera hardware is available
/// (development/demo/container testing) - selected globally via Camera:SimulatorMode.
/// </summary>

/// <summary>Hikvision-style ISAPI HTTP snapshot: GET /ISAPI/Streaming/channels/{ch}01/picture (HTTP Digest/Basic auth).</summary>
public class IsapiCaptureProvider : ICameraCaptureProvider
{
    public string Protocol => "ISAPI";

    public async Task<(bool success, byte[]? jpegBytes, string? error)> CaptureAsync(CameraConfig camera, string? plainPassword, CancellationToken ct)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                Credentials = new NetworkCredential(camera.Username ?? "", plainPassword ?? "")
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            var url = $"http://{camera.IpAddress}:{camera.Port}/ISAPI/Streaming/channels/{camera.Channel}01/picture";
            var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
                return (false, null, $"ISAPI camera at {camera.IpAddress} returned HTTP {(int)resp.StatusCode}. Check credentials/channel.");
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length > 100 ? (true, bytes, null) : (false, null, "ISAPI camera returned an empty image.");
        }
        catch (TaskCanceledException)
        {
            return (false, null, $"ISAPI camera at {camera.IpAddress}:{camera.Port} timed out.");
        }
        catch (Exception ex)
        {
            return (false, null, $"ISAPI capture failed: {ex.Message}");
        }
    }
}

/// <summary>Standard ONVIF Profile S: WS-Security PasswordDigest GetSnapshotUri call, then HTTP GET of the returned URI.</summary>
public class OnvifCaptureProvider : ICameraCaptureProvider, ICameraContinuousStreamProvider
{
    private readonly RtspCaptureProvider _rtsp;
    private readonly ILogger<OnvifCaptureProvider> _log;

    public OnvifCaptureProvider(RtspCaptureProvider rtsp, ILogger<OnvifCaptureProvider> log)
    {
        _rtsp = rtsp;
        _log = log;
    }

    public string Protocol => "ONVIF";

    public async Task<(bool success, byte[]? jpegBytes, string? error)> CaptureAsync(CameraConfig camera, string? plainPassword, CancellationToken ct)
    {
        try
        {
            var (mediaUrl, profileToken) = ResolveTarget(camera);
            var envelope = BuildGetSnapshotUriEnvelope(camera.Username ?? "", plainPassword ?? "", profileToken);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var req = new HttpRequestMessage(HttpMethod.Post, mediaUrl)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml")
            };
            var resp = await http.SendAsync(req, ct);
            var xml = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (false, null, $"ONVIF GetSnapshotUri at {camera.IpAddress} returned HTTP {(int)resp.StatusCode}.");
            var uri = ExtractTag(xml, "Uri");
            if (string.IsNullOrWhiteSpace(uri))
                return (false, null, "ONVIF camera did not return a snapshot URI. Verify OnvifSettings override (mediaServiceUrl/profileToken).");

            using var imgHandler = new HttpClientHandler { Credentials = new NetworkCredential(camera.Username ?? "", plainPassword ?? "") };
            using var imgClient = new HttpClient(imgHandler) { Timeout = TimeSpan.FromSeconds(8) };
            var imgResp = await imgClient.GetAsync(uri, ct);
            if (!imgResp.IsSuccessStatusCode)
                return (false, null, $"ONVIF snapshot fetch returned HTTP {(int)imgResp.StatusCode}.");
            var bytes = await imgResp.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length > 100 ? (true, bytes, null) : (false, null, "ONVIF camera returned an empty image.");
        }
        catch (TaskCanceledException)
        {
            return (false, null, $"ONVIF camera at {camera.IpAddress}:{camera.Port} timed out.");
        }
        catch (Exception ex)
        {
            return (false, null, $"ONVIF capture failed: {ex.Message}");
        }
    }

    /// <summary>
    /// ONVIF is used for discovery only. Once a stream URI is discovered, the
    /// same persistent RTSP decoder used by the RTSP provider renders it.
    /// </summary>
    public async IAsyncEnumerable<byte[]> StreamAsync(
        CameraConfig camera, string? plainPassword,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Explicit user override always wins; ONVIF discovery is only needed
        // when no complete RTSP URL was supplied.
        var discovered = string.IsNullOrWhiteSpace(camera.RtspUrl)
            ? await TryDiscoverStreamUriAsync(camera, plainPassword, ct)
            : null;
        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(camera.RtspUrl))
            urls.Add(camera.RtspUrl!);
        if (!string.IsNullOrWhiteSpace(discovered))
            urls.Add(NormalizeDiscoveredUri(discovered!));
        urls.AddRange(RtspCaptureProvider.BuildRtspUrls(camera, plainPassword));

        foreach (var url in urls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ct.IsCancellationRequested) yield break;
            await foreach (var frame in _rtsp.StreamExternalUrlAsync(camera, url, plainPassword, ct))
                yield return frame;
        }
    }

    private async Task<string?> TryDiscoverStreamUriAsync(
        CameraConfig camera, string? plainPassword, CancellationToken ct)
    {
        try
        {
            var settings = ReadOnvifSettings(camera.OnvifSettings);
            var mediaUrl = settings.mediaServiceUrl;

            if (string.IsNullOrWhiteSpace(mediaUrl))
            {
                foreach (var deviceUrl in DeviceServiceCandidates(camera, settings.deviceServiceUrl))
                {
                    try
                    {
                        var capabilities = await SendSoapAsync(
                            deviceUrl,
                            BuildGetCapabilitiesEnvelope(camera.Username ?? "", plainPassword ?? ""),
                            camera, ct);
                        mediaUrl = ExtractMediaXAddr(capabilities);
                        if (!string.IsNullOrWhiteSpace(mediaUrl)) break;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                    {
                        _log.LogDebug("ONVIF capabilities probe failed for camera {CameraNumber} at {Host}: {Error}",
                            camera.CameraNumber, camera.IpAddress, ex.Message);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(mediaUrl)) return null;

            var profilesXml = await SendSoapAsync(
                mediaUrl,
                BuildGetProfilesEnvelope(camera.Username ?? "", plainPassword ?? ""),
                camera, ct);
            var profileToken = settings.profileToken;
            if (string.IsNullOrWhiteSpace(profileToken))
                profileToken = SelectProfileToken(profilesXml, camera.StreamType);
            if (string.IsNullOrWhiteSpace(profileToken)) return null;

            var streamXml = await SendSoapAsync(
                mediaUrl,
                BuildGetStreamUriEnvelope(camera.Username ?? "", plainPassword ?? "", profileToken),
                camera, ct);
            var uri = ExtractTag(streamXml, "Uri");
            if (string.IsNullOrWhiteSpace(uri)) return null;

            _log.LogInformation("ONVIF discovered stream for camera {CameraNumber}: {Host} {StreamType}",
                camera.CameraNumber, camera.IpAddress, camera.StreamType);
            return uri;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning("ONVIF stream discovery failed for camera {CameraNumber} at {Host}: {Error}",
                camera.CameraNumber, camera.IpAddress, ex.Message);
            return null;
        }
    }

    private static IEnumerable<string> DeviceServiceCandidates(CameraConfig camera, string? overrideUrl)
    {
        if (!string.IsNullOrWhiteSpace(overrideUrl)) yield return overrideUrl!;
        yield return $"https://{camera.IpAddress}/onvif/device_service";
        yield return $"http://{camera.IpAddress}:{camera.Port}/onvif/device_service";
        yield return $"http://{camera.IpAddress}/onvif/device_service";
    }

    private async Task<string> SendSoapAsync(string url, string envelope, CameraConfig camera, CancellationToken ct)
    {
        using var handler = new HttpClientHandler
        {
            // Camera ONVIF HTTPS commonly uses a self-signed LAN certificate.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml")
        };
        using var resp = await http.SendAsync(req, ct);
        var xml = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"ONVIF endpoint returned HTTP {(int)resp.StatusCode}.");
        return xml;
    }

    private static string NormalizeDiscoveredUri(string uri)
    {
        // CP Plus exposes an ONVIF URI as rtsp://... with tls=true, while its
        // actual secure transport requires the rtsps:// scheme.
        if (uri.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) &&
            uri.Contains("tls=true", StringComparison.OrdinalIgnoreCase))
            return "rtsps://" + uri[7..];
        return uri;
    }

    private static (string? deviceServiceUrl, string? mediaServiceUrl, string? profileToken)
        ReadOnvifSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            string? Get(string name) => doc.RootElement.TryGetProperty(name, out var p) &&
                p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            return (Get("deviceServiceUrl"), Get("mediaServiceUrl"), Get("profileToken"));
        }
        catch { return (null, null, null); }
    }

    private static string? ExtractMediaXAddr(string xml)
    {
        var match = Regex.Match(xml,
            @"<(?:[^:>]+:)?Media\b[^>]*>.*?<(?:[^:>]+:)?XAddr[^>]*>([^<]+)</",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) : null;
    }

    private static string? SelectProfileToken(string xml, string streamType)
    {
        var profiles = Regex.Matches(xml,
            @"<(?<tag>(?:[^:>]+:)?Profiles?)\b[^>]*\btoken=[""'](?<token>[^""']+)[""'][^>]*>(?<body>.*?)</\k<tag>>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var wantSub = string.Equals(streamType, "Sub", StringComparison.OrdinalIgnoreCase);
        string? first = null;
        foreach (Match profile in profiles)
        {
            first ??= profile.Groups["token"].Value;
            var name = ExtractTag(profile.Groups["body"].Value, "Name");
            if (wantSub == name.Contains("sub", StringComparison.OrdinalIgnoreCase))
                return profile.Groups["token"].Value;
        }
        return first;
    }

    private static string BuildWsseHeader(string username, string password)
    {
        var created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var nonceBytes = new byte[16];
        RandomNumberGenerator.Fill(nonceBytes);
        var nonce = Convert.ToBase64String(nonceBytes);
        var digestSource = nonceBytes.Concat(Encoding.UTF8.GetBytes(created)).Concat(Encoding.UTF8.GetBytes(password)).ToArray();
        var digest = Convert.ToBase64String(SHA1.HashData(digestSource));
        return $@"<s:Header>
    <Security xmlns=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"">
      <UsernameToken>
        <Username>{SecurityElement.Escape(username)}</Username>
        <Password Type=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest"">{digest}</Password>
        <Nonce EncodingType=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"">{nonce}</Nonce>
        <Created xmlns=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wsu-utility-1.0.xsd"">{created}</Created>
      </UsernameToken>
    </Security>
  </s:Header>";
    }

    private static string BuildGetCapabilitiesEnvelope(string username, string password) =>
        BuildEnvelope(BuildWsseHeader(username, password),
            @"<tds:GetCapabilities xmlns:tds=""http://www.onvif.org/ver10/device/wsdl""><tds:Category>All</tds:Category></tds:GetCapabilities>");

    private static string BuildGetProfilesEnvelope(string username, string password) =>
        BuildEnvelope(BuildWsseHeader(username, password),
            @"<trt:GetProfiles xmlns:trt=""http://www.onvif.org/ver10/media/wsdl""/>");

    private static string BuildGetStreamUriEnvelope(string username, string password, string profileToken) =>
        BuildEnvelope(BuildWsseHeader(username, password),
            $@"<trt:GetStreamUri xmlns:trt=""http://www.onvif.org/ver10/media/wsdl""><trt:StreamSetup><tt:Stream xmlns:tt=""http://www.onvif.org/ver10/schema"">RTP-Unicast</tt:Stream><tt:Transport xmlns:tt=""http://www.onvif.org/ver10/schema""><tt:Protocol>RTSP</tt:Protocol></tt:Transport></trt:StreamSetup><trt:ProfileToken>{SecurityElement.Escape(profileToken)}</trt:ProfileToken></trt:GetStreamUri>");

    private static string BuildEnvelope(string header, string body) =>
        $@"<?xml version=""1.0"" encoding=""UTF-8""?><s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope""><s:Header>{header.Replace("<s:Header>", "").Replace("</s:Header>", "")}</s:Header><s:Body>{body}</s:Body></s:Envelope>";

    /// <summary>Default device_service + Profile_{channel}; a camera's OnvifSettings JSON can override
    /// {"mediaServiceUrl":"...","profileToken":"..."} for models with non-standard ONVIF paths.</summary>
    private static (string mediaUrl, string profileToken) ResolveTarget(CameraConfig camera)
    {
        var mediaUrl = $"http://{camera.IpAddress}:{camera.Port}/onvif/device_service";
        var profileToken = $"Profile_{camera.Channel}";
        if (!string.IsNullOrWhiteSpace(camera.OnvifSettings))
        {
            try
            {
                using var doc = JsonDocument.Parse(camera.OnvifSettings);
                if (doc.RootElement.TryGetProperty("mediaServiceUrl", out var m) && m.ValueKind == JsonValueKind.String)
                    mediaUrl = m.GetString()!;
                if (doc.RootElement.TryGetProperty("profileToken", out var pt) && pt.ValueKind == JsonValueKind.String)
                    profileToken = pt.GetString()!;
            }
            catch { /* malformed override JSON - fall back to defaults */ }
        }
        return (mediaUrl, profileToken);
    }

    private static string ExtractTag(string xml, string tag)
    {
        var m = System.Text.RegularExpressions.Regex.Match(xml, $@"<[^:>]*:?{tag}[^>]*>([^<]+)</",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    private static string BuildGetSnapshotUriEnvelope(string username, string password, string profileToken)
    {
        var created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var nonceBytes = new byte[16];
        RandomNumberGenerator.Fill(nonceBytes);
        var nonce = Convert.ToBase64String(nonceBytes);
        var digestSource = nonceBytes.Concat(Encoding.UTF8.GetBytes(created)).Concat(Encoding.UTF8.GetBytes(password)).ToArray();
        var digest = Convert.ToBase64String(SHA1.HashData(digestSource));
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope"">
  <s:Header>
    <Security xmlns=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"">
      <UsernameToken>
        <Username>{username}</Username>
        <Password Type=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest"">{digest}</Password>
        <Nonce EncodingType=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"">{nonce}</Nonce>
        <Created xmlns=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wsu-utility-1.0.xsd"">{created}</Created>
      </UsernameToken>
    </Security>
  </s:Header>
  <s:Body>
    <GetSnapshotUri xmlns=""http://www.onvif.org/ver10/media/wsdl"">
      <ProfileToken>{profileToken}</ProfileToken>
    </GetSnapshotUri>
  </s:Body>
</s:Envelope>";
    }
}

/// <summary>Generic RTSP: extracts a single frame via the ffmpeg binary (must be installed and on PATH).
/// Works with any RTSP-capable model (CP Plus/Dahua/Uniview/Generic) using vendor URL templates.</summary>
public class RtspCaptureProvider : ICameraCaptureProvider, ICameraContinuousStreamProvider
{
    private readonly ILogger<RtspCaptureProvider> _log;

    public RtspCaptureProvider(ILogger<RtspCaptureProvider> log) => _log = log;

    public string Protocol => "RTSP";

    public async Task<(bool success, byte[]? jpegBytes, string? error)> CaptureAsync(CameraConfig camera, string? plainPassword, CancellationToken ct)
    {
        var errors = new List<string>();
        foreach (var url in BuildRtspUrls(camera, plainPassword))
        {
            var result = await CaptureRtspFrameAsync(camera, url, ct);
            if (result.success) return result;
            if (!string.IsNullOrWhiteSpace(result.error)) errors.Add(result.error!);
        }

        return (false, null, string.Join(" | ", errors.Distinct()));
    }

    /// <summary>
    /// Opens one long-lived ffmpeg decoder and emits JPEG frames from its MJPEG
    /// stdout. The caller owns cancellation; no process is created per frame.
    /// </summary>
    public async IAsyncEnumerable<byte[]> StreamAsync(
        CameraConfig camera, string? plainPassword,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        var urls = BuildRtspUrls(camera, plainPassword).ToList();

        while (!ct.IsCancellationRequested)
        {
            var producedFrame = false;
            foreach (var url in urls)
            {
                if (ct.IsCancellationRequested) yield break;
                var startedAt = Stopwatch.GetTimestamp();
                _log.LogInformation("Opening live camera stream {CameraNumber} {Host}:{Port} {Protocol} {StreamType}",
                    camera.CameraNumber, camera.IpAddress, camera.Port,
                    Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Scheme : "RTSP",
                    camera.StreamType);

                await foreach (var frame in StreamUrlAsync(camera, url, ct))
                {
                    if (!producedFrame)
                    {
                        producedFrame = true;
                        _log.LogInformation("Live camera {CameraNumber} produced its first frame in {ElapsedMs} ms",
                            camera.CameraNumber, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                    }
                    retryDelay = TimeSpan.FromSeconds(1);
                    yield return frame;
                }

                // Once a URL has produced frames, reconnect that URL rather
                // than cycling through every vendor fallback on every drop.
                if (producedFrame) break;
            }

            if (ct.IsCancellationRequested) yield break;
            _log.LogWarning("Live camera {CameraNumber} disconnected; retrying in {RetrySeconds} seconds",
                camera.CameraNumber, retryDelay.TotalSeconds);
            try { await Task.Delay(retryDelay, ct); }
            catch (OperationCanceledException) { yield break; }
            retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 10));
        }
    }

    /// <summary>Streams a URI discovered by ONVIF or supplied as an explicit override.</summary>
    public IAsyncEnumerable<byte[]> StreamExternalUrlAsync(
        CameraConfig camera, string url, string? plainPassword,
        CancellationToken ct = default)
        => StreamUrlAsync(camera, InjectCredentials(url, camera.Username, plainPassword), ct);

    private async IAsyncEnumerable<byte[]> StreamUrlAsync(
        CameraConfig camera, string url,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CANE_FFMPEG_PATH"))
                ? "ffmpeg"
                : Environment.GetEnvironmentVariable("CANE_FFMPEG_PATH")!,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-nostdin",
            "-fflags", "nobuffer", "-flags", "low_delay",
            "-analyzeduration", "500000", "-probesize", "32768"
        };
        if (url.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase))
            args.AddRange(["-tls_verify", "0"]);
        args.AddRange([
            "-rtsp_transport", "tcp", "-i", url,
            "-an", "-vf", "scale=640:-2", "-r", "10",
            "-c:v", "mjpeg", "-q:v", "6", "-f", "mjpeg", "pipe:1"
        ]);
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        Process? process = null;
        try
        {
            process = Process.Start(psi);
            if (process == null) yield break;
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            var stdout = process.StandardOutput.BaseStream;
            var readBuffer = new byte[64 * 1024];
            var pending = new List<byte>(128 * 1024);
            var fpsWindowStarted = Stopwatch.GetTimestamp();
            var fpsWindowFrames = 0;

            while (!ct.IsCancellationRequested)
            {
                var read = await stdout.ReadAsync(readBuffer.AsMemory(), ct);
                if (read == 0) break;
                for (var i = 0; i < read; i++) pending.Add(readBuffer[i]);

                while (TryTakeJpeg(pending, out var jpeg))
                {
                    fpsWindowFrames++;
                    var fpsWindow = Stopwatch.GetElapsedTime(fpsWindowStarted);
                    if (fpsWindow >= TimeSpan.FromSeconds(5))
                    {
                        _log.LogDebug("Live camera {CameraNumber} decoder FPS {Fps:F1}",
                            camera.CameraNumber, fpsWindowFrames / fpsWindow.TotalSeconds);
                        fpsWindowStarted = Stopwatch.GetTimestamp();
                        fpsWindowFrames = 0;
                    }
                    yield return jpeg;
                }
            }

            if (!ct.IsCancellationRequested)
            {
                var stderr = (await stderrTask).Trim();
                if (!string.IsNullOrWhiteSpace(stderr))
                    _log.LogWarning("Live camera {CameraNumber} ffmpeg stopped: {Error}",
                        camera.CameraNumber, RedactRtspCredentials(stderr, url));
            }
        }
        finally
        {
            if (process != null)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch { /* client disconnect cleanup is best effort */ }
                process.Dispose();
            }
        }
    }

    private static bool TryTakeJpeg(List<byte> pending, out byte[] jpeg)
    {
        jpeg = Array.Empty<byte>();
        var start = FindMarker(pending, 0, 0xFF, 0xD8);
        if (start < 0)
        {
            if (pending.Count > 1) pending.RemoveRange(0, pending.Count - 1);
            return false;
        }
        if (start > 0) pending.RemoveRange(0, start);
        var end = FindMarker(pending, 2, 0xFF, 0xD9);
        if (end < 0) return false;
        var length = end + 2;
        jpeg = pending.GetRange(0, length).ToArray();
        pending.RemoveRange(0, length);
        return jpeg.Length > 100;
    }

    private static int FindMarker(List<byte> bytes, int start, byte first, byte second)
    {
        for (var i = start; i + 1 < bytes.Count; i++)
            if (bytes[i] == first && bytes[i + 1] == second) return i;
        return -1;
    }

    private static async Task<(bool success, byte[]? jpegBytes, string? error)> CaptureRtspFrameAsync(
        CameraConfig camera, string url, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"cam_{camera.Id}_{Guid.NewGuid():N}.jpg");
        try
        {
            var psi = new ProcessStartInfo
            {
                // Use an explicit path when the API runs as a Windows service
                // whose PATH does not include the interactive user's tools.
                // Otherwise resolve the normal ffmpeg command from PATH.
                FileName = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CANE_FFMPEG_PATH"))
                    ? "ffmpeg"
                    : Environment.GetEnvironmentVariable("CANE_FFMPEG_PATH")!,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
            if (url.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase))
            {
                // CP Plus secure RTSP uses a self-signed device certificate on
                // the private factory LAN.
                args.AddRange(["-tls_verify", "0"]);
            }
            args.AddRange(["-rtsp_transport", "tcp", "-i", url, "-frames:v", "1", "-q:v", "3", tempFile]);
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            if (proc == null) return (false, null, "Could not start the ffmpeg process.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var errorOutput = proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);
            var ffmpegError = RedactRtspCredentials((await errorOutput).Trim(), url);

            if (!File.Exists(tempFile))
                return (false, null, string.IsNullOrWhiteSpace(ffmpegError)
                    ? "RTSP snapshot failed - ffmpeg did not produce an image. Check the RTSP URL/credentials/network."
                    : $"RTSP snapshot failed: {ffmpegError}");
            var bytes = await File.ReadAllBytesAsync(tempFile, ct);
            return bytes.Length > 100 ? (true, bytes, null) : (false, null, "RTSP camera returned an empty image.");
        }
        catch (OperationCanceledException)
        {
            return (false, null, "RTSP snapshot timed out (camera unreachable or stream too slow).");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (false, null, "ffmpeg is not installed on this server. Install ffmpeg (add to PATH) for RTSP capture, or switch this camera to ISAPI/ONVIF.");
        }
        catch (Exception ex)
        {
            return (false, null, $"RTSP capture failed: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* best effort cleanup */ }
        }
    }

    public static string BuildRtspUrl(CameraConfig camera, string? plainPassword)
        => BuildRtspUrls(camera, plainPassword).First();

    public static IEnumerable<string> BuildRtspUrls(CameraConfig camera, string? plainPassword)
    {
        if (!string.IsNullOrWhiteSpace(camera.RtspUrl))
        {
            yield return InjectCredentials(camera.RtspUrl, camera.Username, plainPassword);
            yield break;
        }
        var cred = string.IsNullOrEmpty(camera.Username) ? "" : $"{Uri.EscapeDataString(camera.Username)}:{Uri.EscapeDataString(plainPassword ?? "")}@";
        if (camera.Vendor is "CPPlus" or "Dahua")
        {
            var subtype = string.Equals(camera.StreamType, "Sub", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            // CP-UNC-TA61L3C-LQ exposes this secure URI through ONVIF
            // GetStreamUri. This is the verified path for direct camera access.
            yield return $"rtsps://{cred}{camera.IpAddress}:{camera.Port}/video/live?channel={camera.Channel}&subtype={subtype}&unicast=true&proto=Onvif&tls=true";
            yield return $"rtsps://{cred}{camera.IpAddress}:{camera.Port}/video/live?channel={camera.Channel}&subtype={(subtype == 0 ? 1 : 0)}&unicast=true&proto=Onvif&tls=true";
            // Compatibility fallback for older CP Plus/Dahua firmware.
            yield return $"rtsp://{cred}{camera.IpAddress}:{camera.Port}/cam/realmonitor?channel={camera.Channel}&subtype={subtype}";
            yield return $"rtsp://{cred}{camera.IpAddress}:{camera.Port}/cam/realmonitor?channel={camera.Channel}&subtype={(subtype == 0 ? 1 : 0)}";
            yield break;
        }
        if (camera.Vendor == "Hikvision")
        {
            yield return $"rtsp://{cred}{camera.IpAddress}:{camera.Port}/Streaming/Channels/{camera.Channel}01";
            yield break;
        }
        if (camera.Vendor == "Uniview")
        {
            yield return $"rtsp://{cred}{camera.IpAddress}:{camera.Port}/media/video{camera.Channel}";
            yield break;
        }
        yield return $"rtsp://{cred}{camera.IpAddress}:{camera.Port}/";
    }

    private static string InjectCredentials(string rtspUrl, string? username, string? password)
    {
        if (string.IsNullOrEmpty(username) || rtspUrl.Contains('@')) return rtspUrl;
        var scheme = rtspUrl.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase) ? "rtsps://" : "rtsp://";
        return rtspUrl.Replace(scheme, $"{scheme}{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password ?? "")}@", StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactRtspCredentials(string error, string url)
    {
        if (string.IsNullOrWhiteSpace(error)) return error;
        var safeUrl = url;
        try
        {
            var parsed = new Uri(url);
            safeUrl = $"{parsed.Scheme}://{parsed.Host}{(parsed.IsDefaultPort ? "" : $":{parsed.Port}")}{parsed.PathAndQuery}";
        }
        catch { /* keep the original error text if the camera URL is malformed */ }
        var redacted = error.Replace(url, safeUrl, StringComparison.Ordinal);
        return Regex.Replace(redacted, @"(rtsps?://)[^/\s@]+@", "$1<redacted>@",
            RegexOptions.IgnoreCase);
    }
}

/// <summary>Used only when Camera:SimulatorMode=true (no physical camera hardware, e.g. this container).
/// Returns a fixed placeholder JPEG so the full pipeline (save/hash/DB/serve) is exercised end-to-end.</summary>
public class SimulatorCaptureProvider : ICameraCaptureProvider
{
    public string Protocol => "SIMULATOR";

    private static readonly byte[] PlaceholderJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAoHBwgHBgoICAgLCgoLDhgQDg0NDh0VFhEYIx8lJCIfIiEmKzcvJik0KSEiMEExNDk7Pj4+JS5ESUM8SDc9Pjv/2wBDAQoLCw4NDhwQEBw7KCIoOzs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozv/wAARCADwAUADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDH1TVP7N8r9z5nmZ/ixjGPb3rP/wCEo/6c/wDyL/8AWo8Uf8uv/A//AGWsGvIweDoVKEZSjrr1fc8LAYDD1cPGc43bv1fd+Zvf8JR/05/+Rf8A61H/AAlH/Tn/AORf/rVg0V1fUMN/L+L/AMzs/svCfyfi/wDM3v8AhKP+nP8A8i//AFqP+Eo/6c//ACL/APWrBoo+oYb+X8X/AJh/ZeE/k/F/5m9/wlH/AE5/+Rf/AK1H/CUf9Of/AJF/+tWDRR9Qw38v4v8AzD+y8J/J+L/zN7/hKP8Apz/8i/8A1qP+Eo/6c/8AyL/9asGij6hhv5fxf+Yf2XhP5Pxf+Zvf8JR/05/+Rf8A61H/AAlH/Tn/AORf/rVg0UfUMN/L+L/zD+y8J/J+L/zN7/hKP+nP/wAi/wD1qP8AhKP+nP8A8i//AFqwaKPqGG/l/F/5h/ZeE/k/F/5m9/wlH/Tn/wCRf/rUf8JR/wBOf/kX/wCtWDRR9Qw38v4v/MP7Lwn8n4v/ADN7/hKP+nP/AMi//Wo/4Sj/AKc//Iv/ANasGij6hhv5fxf+Yf2XhP5Pxf8Amb3/AAlH/Tn/AORf/rUf8JR/05/+Rf8A61YNFH1DDfy/i/8AMP7Lwn8n4v8AzN7/AISj/pz/APIv/wBaj/hKP+nP/wAi/wD1qwaKPqGG/l/F/wCYf2XhP5Pxf+Zvf8JR/wBOf/kX/wCtR/wlH/Tn/wCRf/rVg0UfUMN/L+L/AMw/svCfyfi/8ze/4Sj/AKc//Iv/ANaj/hKP+nP/AMi//WrBoo+oYb+X8X/mH9l4T+T8X/mb3/CUf9Of/kX/AOtR/wAJR/05/wDkX/61YNFH1DDfy/i/8w/svCfyfi/8ze/4Sj/pz/8AIv8A9aj/AISj/pz/APIv/wBasGij6hhv5fxf+Yf2XhP5Pxf+Zvf8JR/05/8AkX/61H/CUf8ATn/5F/8ArVg0UfUMN/L+L/zD+y8J/J+L/wAze/4Sj/pz/wDIv/1qP+Eo/wCnP/yL/wDWrBoo+oYb+X8X/mH9l4T+T8X/AJm9/wAJR/05/wDkX/61H/CUf9Of/kX/AOtWDRR9Qw38v4v/ADD+y8J/J+L/AMze/wCEo/6c/wDyL/8AWo/4Sj/pz/8AIv8A9asGij6hhv5fxf8AmH9l4T+T8X/mb3/CUf8ATn/5F/8ArVoaXqn9peb+58vy8fxZznPt7VyNb3hf/l6/4B/7NXLjMHQp0JSjHXTq+5x4/AYelh5ThGzVur7rzDxR/wAuv/A//Zawa3vFH/Lr/wAD/wDZawa6sB/u0fn+bOzK/wDdIfP82FFFFdp6IUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABW94X/wCXr/gH/s1YNb3hf/l6/wCAf+zVxY//AHaXy/NHnZp/uk/l+aDxR/y6/wDA/wD2WsGt7xR/y6/8D/8AZawaMB/u0fn+bDK/90h8/wA2FFFFdp6IUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABW94X/AOXr/gH/ALNWDW94X/5ev+Af+zVxY/8A3aXy/NHnZp/uk/l+aDxR/wAuv/A//Zawa3vFH/Lr/wAD/wDZawaMB/u0fn+bDK/90h8/zYUUUV2nohRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFb3hf/l6/wCAf+zVg1veF/8Al6/4B/7NXFj/APdpfL80edmn+6T+X5oPFH/Lr/wP/wBlrBre8Uf8uv8AwP8A9lrBowH+7R+f5sMr/wB0h8/zYUUUV2nohRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFb3hf/l6/4B/7NWDW94X/AOXr/gH/ALNXFj/92l8vzR52af7pP5fmg8Uf8uv/AAP/ANlrBre8Uf8ALr/wP/2WsGjAf7tH5/mwyv8A3SHz/NhRRRXaeiFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAVveF/8Al6/4B/7NWDW94X/5ev8AgH/s1cWP/wB2l8vzR52af7pP5fmg8Uf8uv8AwP8A9lrBre8Uf8uv/A//AGWsGjAf7tH5/mwyv/dIfP8ANhRRRXaeiFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAUUUUAFFFFABRRRQAVveF/wDl6/4B/wCzVg1veF/+Xr/gH/s1cWP/AN2l8vzR52af7pP5fmg8Uf8ALr/wP/2WsGt7xR/y6/8AA/8A2WsGjAf7tH5/mwyv/dIfP82FFFFdp6IUUUUAFFFFAD4Mi4jICsdw4Y8HnvV+YN9nmZzICYj8sv3l+dO/cen41m0VlOnzST7GM6XPJSvsbU2Ptd1P3lWWP/vkNn9Av51UlUiKaUg7Ht41VuxI2ZH6H8qoUVnChy21/pGUMNyW17fh/wAMaEsDrqs0ssbrEsrvuK8EAk/jSrK4uzJFK4WaB3znGSEYEkZ65BNZ1FV7G6s30sV7C6s30tsaExmewhfzLggxndgEqfnbqc06+87fefaN+zzD5W/13dvbGf0rNooVGzvf8PO4LD2d79X087l+5W4JlVRm3JAiB6EZGNvvj+tWI3RZlkiJY2quhyuBjYcd/UE/jWRRSdC8bX/rYTw148t/w7qxsRIIPJhU5Au4nz9S2P0AP41Chlj89pJrpf3PDupDD516c/1rNopKhrdv8BLDO7be/karBN7o7HN1hV+X7w2jBPPGSQe/SsqiitKdPk6mtKl7PrcKKKK1NgooooAKKKKACiiigAooooAKKKKACiiigAooooAK3vC//L1/wD/2asGt7wv/AMvX/AP/AGauLH/7tL5fmjzs0/3Sfy/NB4o/5df+B/8AstYNb3ij/l1/4H/7LWDRgP8Ado/P82GV/wC6Q+f5sKKKK7T0QooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACt7wv/wAvX/AP/Zqwa3vC/wDy9f8AAP8A2auLH/7tL5fmjzs0/wB0n8vzQeKP+XX/AIH/AOy1g1veKP8Al1/4H/7LWDRgP92j8/zYZX/ukPn+bCiiiu09EKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAre8L/wDL1/wD/wBmrBre8L/8vX/AP/Zq4sf/ALtL5fmjzs0/3Sfy/NB4o/5df+B/+y1g1veKP+XX/gf/ALLWDRgP92j8/wA2GV/7pD5/mwooortPRCiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAK3vC/wDy9f8AAP8A2asGt7wv/wAvX/AP/Zq4sf8A7tL5fmjzs0/3Sfy/NB4o/wCXX/gf/stYNb3ij/l1/wCB/wDstYNGA/3aPz/Nhlf+6Q+f5sKKKK7T0QooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACt7wv/y9f8A/9mrBre8L/wDL1/wD/wBmrix/+7S+X5o87NP90n8vzQeKP+XX/gf/ALLWDW94o/5df+B/+y1g0YD/AHaPz/Nhlf8AukPn+bCiiiu09EKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAre8L/8AL1/wD/2asGt7wv8A8vX/AAD/ANmrix/+7S+X5o87NP8AdJ/L80Hij/l1/wCB/wDstYNb3ij/AJdf+B/+y1g0YD/do/P82GV/7pD5/mwooortPRCiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAK3vC/8Ay9f8A/8AZqwa3vC//L1/wD/2auLH/wC7S+X5o87NP90n8vzQeKP+XX/gf/stYNddqml/2l5X77y/Lz/DnOce/tWf/wAIv/0+f+Qv/r1y4PGUKdCMZS116PuceAx+HpYeMJys1fo+78jBore/4Rf/AKfP/IX/ANej/hF/+nz/AMhf/Xrq+v4b+b8H/kdn9qYT+f8AB/5GDRW9/wAIv/0+f+Qv/r0f8Iv/ANPn/kL/AOvR9fw3834P/IP7Uwn8/wCD/wAjBore/wCEX/6fP/IX/wBej/hF/wDp8/8AIX/16Pr+G/m/B/5B/amE/n/B/wCRg0Vvf8Iv/wBPn/kL/wCvR/wi/wD0+f8AkL/69H1/Dfzfg/8AIP7Uwn8/4P8AyMGit7/hF/8Ap8/8hf8A16P+EX/6fP8AyF/9ej6/hv5vwf8AkH9qYT+f8H/kYNFb3/CL/wDT5/5C/wDr0f8ACL/9Pn/kL/69H1/Dfzfg/wDIP7Uwn8/4P/IwaK3v+EX/AOnz/wAhf/Xo/wCEX/6fP/IX/wBej6/hv5vwf+Qf2phP5/wf+Rg0Vvf8Iv8A9Pn/AJC/+vR/wi//AE+f+Qv/AK9H1/Dfzfg/8g/tTCfz/g/8jBore/4Rf/p8/wDIX/16P+EX/wCnz/yF/wDXo+v4b+b8H/kH9qYT+f8AB/5GDRW9/wAIv/0+f+Qv/r0f8Iv/ANPn/kL/AOvR9fw3834P/IP7Uwn8/wCD/wAjBore/wCEX/6fP/IX/wBej/hF/wDp8/8AIX/16Pr+G/m/B/5B/amE/n/B/wCRg0Vvf8Iv/wBPn/kL/wCvR/wi/wD0+f8AkL/69H1/Dfzfg/8AIP7Uwn8/4P8AyMGit7/hF/8Ap8/8hf8A16P+EX/6fP8AyF/9ej6/hv5vwf8AkH9qYT+f8H/kYNFb3/CL/wDT5/5C/wDr0f8ACL/9Pn/kL/69H1/Dfzfg/wDIP7Uwn8/4P/IwaK3v+EX/AOnz/wAhf/Xo/wCEX/6fP/IX/wBej6/hv5vwf+Qf2phP5/wf+Rg0Vvf8Iv8A9Pn/AJC/+vR/wi//AE+f+Qv/AK9H1/Dfzfg/8g/tTCfz/g/8jBore/4Rf/p8/wDIX/16P+EX/wCnz/yF/wDXo+v4b+b8H/kH9qYT+f8AB/5GDRW9/wAIv/0+f+Qv/r0f8Iv/ANPn/kL/AOvR9fw3834P/IP7Uwn8/wCD/wAjBre8L/8AL1/wD/2aj/hF/wDp8/8AIX/160NL0v8As3zf33meZj+HGMZ9/euXGYyhUoSjGWunR9zjx+Pw9XDyhCV27dH3Xkf/2Q==");

    public Task<(bool success, byte[]? jpegBytes, string? error)> CaptureAsync(CameraConfig camera, string? plainPassword, CancellationToken ct)
        => Task.FromResult<(bool, byte[]?, string?)>((true, PlaceholderJpeg, null));
}
