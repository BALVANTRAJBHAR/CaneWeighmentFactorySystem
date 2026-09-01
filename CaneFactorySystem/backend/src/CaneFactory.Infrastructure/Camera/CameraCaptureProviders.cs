using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;

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
public class OnvifCaptureProvider : ICameraCaptureProvider
{
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
public class RtspCaptureProvider : ICameraCaptureProvider
{
    public string Protocol => "RTSP";

    public async Task<(bool success, byte[]? jpegBytes, string? error)> CaptureAsync(CameraConfig camera, string? plainPassword, CancellationToken ct)
    {
        var url = BuildRtspUrl(camera, plainPassword);
        var tempFile = Path.Combine(Path.GetTempPath(), $"cam_{camera.Id}_{Guid.NewGuid():N}.jpg");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            foreach (var arg in new[] { "-y", "-rtsp_transport", "tcp", "-i", url, "-frames:v", "1", "-q:v", "3", tempFile })
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            if (proc == null) return (false, null, "Could not start the ffmpeg process.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            await proc.WaitForExitAsync(cts.Token);

            if (!File.Exists(tempFile))
                return (false, null, "RTSP snapshot failed - ffmpeg did not produce an image. Check the RTSP URL/credentials/network.");
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
    {
        if (!string.IsNullOrWhiteSpace(camera.RtspUrl)) return InjectCredentials(camera.RtspUrl, camera.Username, plainPassword);
        var cred = string.IsNullOrEmpty(camera.Username) ? "" : $"{Uri.EscapeDataString(camera.Username)}:{Uri.EscapeDataString(plainPassword ?? "")}@";
        return camera.Vendor switch
        {
            "Hikvision" => $"rtsp://{cred}{camera.IpAddress}:{camera.Port}/Streaming/Channels/{camera.Channel}01",
            "CPPlus" or "Dahua" => $"rtsp://{cred}{camera.IpAddress}:{camera.Port}/cam/realmonitor?channel={camera.Channel}&subtype=0",
            "Uniview" => $"rtsp://{cred}{camera.IpAddress}:{camera.Port}/media/video{camera.Channel}",
            _ => $"rtsp://{cred}{camera.IpAddress}:{camera.Port}/"
        };
    }

    private static string InjectCredentials(string rtspUrl, string? username, string? password)
    {
        if (string.IsNullOrEmpty(username) || rtspUrl.Contains('@')) return rtspUrl;
        return rtspUrl.Replace("rtsp://", $"rtsp://{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password ?? "")}@");
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
