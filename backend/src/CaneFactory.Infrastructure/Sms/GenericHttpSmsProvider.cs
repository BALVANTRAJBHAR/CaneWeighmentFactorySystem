using System.Text;
using System.Text.Json;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;

namespace CaneFactory.Infrastructure.Sms;

/// <summary>Generic HTTP SMS provider - works with ANY vendor's HTTP API by substituting placeholders
/// ({Mobile} {Message} {ApiKey} {ApiSecret} {SenderId} {EntityId}) into the URL/header/body templates
/// configured in SmsConfig. No provider (MSG91/Twilio/Fast2SMS/etc) is ever hard-coded.</summary>
public class GenericHttpSmsProvider : ISmsProviderClient
{
    private readonly HttpClient _http;

    public GenericHttpSmsProvider(HttpClient http) => _http = http;

    public async Task<(bool success, string? providerResponse, string? error)> SendAsync(
        SmsConfig cfg, string? apiKey, string? apiSecret, string mobileNumber, string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cfg.ApiBaseUrl))
            return (false, null, "SMS provider API Base URL is not configured.");

        var vars = new Dictionary<string, string>
        {
            ["Mobile"] = mobileNumber,
            ["Message"] = message,
            ["ApiKey"] = apiKey ?? "",
            ["ApiSecret"] = apiSecret ?? "",
            ["SenderId"] = cfg.SenderId ?? "",
            ["EntityId"] = cfg.EntityId ?? "",
        };

        try
        {
            var url = Substitute(cfg.ApiBaseUrl, vars, urlEncode: true);
            var method = new HttpMethod(string.IsNullOrWhiteSpace(cfg.HttpMethod) ? "POST" : cfg.HttpMethod.ToUpperInvariant());
            using var req = new HttpRequestMessage(method, url);

            if (!string.IsNullOrWhiteSpace(cfg.AuthorizationHeader))
                req.Headers.TryAddWithoutValidation("Authorization", Substitute(cfg.AuthorizationHeader, vars, urlEncode: false));

            if (method != HttpMethod.Get && !string.IsNullOrWhiteSpace(cfg.RequestBodyTemplate))
            {
                var body = Substitute(cfg.RequestBodyTemplate, vars, urlEncode: false);
                var contentType = string.IsNullOrWhiteSpace(cfg.RequestContentType) ? "application/json" : cfg.RequestContentType;
                req.Content = new StringContent(body, Encoding.UTF8, contentType);
            }

            using var resp = await _http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (false, text, $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

            if (string.IsNullOrWhiteSpace(cfg.ResponseSuccessPath))
                return (true, text, null);

            var actual = ExtractJsonField(text, cfg.ResponseSuccessPath);
            var ok = actual != null && (string.IsNullOrWhiteSpace(cfg.ResponseSuccessValue)
                || string.Equals(actual, cfg.ResponseSuccessValue, StringComparison.OrdinalIgnoreCase));
            return (ok, text, ok ? null : $"Provider response field '{cfg.ResponseSuccessPath}' was '{actual}', expected '{cfg.ResponseSuccessValue}'.");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private static string Substitute(string template, Dictionary<string, string> vars, bool urlEncode)
    {
        var result = template;
        foreach (var (k, v) in vars)
            result = result.Replace("{" + k + "}", urlEncode ? Uri.EscapeDataString(v) : EscapeForJson(v));
        return result;
    }

    private static string EscapeForJson(string v) => v.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string? ExtractJsonField(string json, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement;
            foreach (var seg in path.Split('.'))
                if (!el.TryGetProperty(seg, out el)) return null;
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => el.GetRawText(),
                _ => el.GetRawText()
            };
        }
        catch
        {
            return null;
        }
    }
}
