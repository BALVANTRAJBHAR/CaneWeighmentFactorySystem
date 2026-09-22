using System.Globalization;
using System.IO.Ports;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "CANE_SCALE_BRIDGE_");
builder.Services.AddWindowsService(options => options.ServiceName = "CaneFactory Scale Bridge");
if (OperatingSystem.IsWindows())
    ConfigureWindowsEventLog(builder.Logging);
builder.Services.Configure<ScaleBridgeOptions>(builder.Configuration.GetSection("ScaleBridge"));
builder.Services.AddHttpClient<ScaleBridge>(client => client.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddHostedService<ScaleBridgeWorker>();

await builder.Build().RunAsync();

[SupportedOSPlatform("windows")]
static void ConfigureWindowsEventLog(ILoggingBuilder logging)
{
#pragma warning disable CA1416 // Called only through the Windows platform guard above.
    logging.AddEventLog(options => options.SourceName = "CaneFactory Scale Bridge");
#pragma warning restore CA1416
}

sealed class ScaleBridge
{
    private readonly ScaleBridgeOptions _cfg;
    private readonly HttpClient _http;
    private readonly ILogger<ScaleBridge> _logger;
    private readonly List<byte> _buffer = [];
    private decimal _lastWeight;
    private DateTime _lastChangedAt = DateTime.UtcNow;
    private DateTime _nextServerSendAt = DateTime.MinValue;
    private DateTime _lastSerialByteAt = DateTime.UtcNow;
    private DateTime _lastNoDataNoticeAt = DateTime.MinValue;
    private DateTime _lastRawNoticeAt = DateTime.MinValue;

    public ScaleBridge(IOptions<ScaleBridgeOptions> options, HttpClient http, ILogger<ScaleBridge> logger) =>
        (_cfg, _http, _logger) = (options.Value, http, logger);

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var port = new SerialPort(_cfg.ComPort, _cfg.BaudRate, ParseParity(_cfg.Parity), _cfg.DataBits,
                    _cfg.StopBits == 2 ? StopBits.Two : StopBits.One)
                {
                    ReadTimeout = _cfg.ReadTimeoutMs,
                    Handshake = Handshake.None
                };
                port.Open();
                _logger.LogInformation("Connected to local {ComPort} at {BaudRate} baud.", _cfg.ComPort, _cfg.BaudRate);
                _lastSerialByteAt = DateTime.UtcNow;
                // Tell the API that the bridge has opened the local port even before
                // the indicator sends its first complete frame. This is a connection
                // status only: no weight is invented or marked as live.
                await SendAsync(0, false, "CONNECTED", null, stoppingToken);
                using var registration = stoppingToken.Register(port.Close);
                await ReadLoopAsync(port, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "COM connection/read error. Retrying in 5 seconds.");
                await SendAsync(0, false, "DISCONNECTED", ex.Message, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ReadLoopAsync(SerialPort port, CancellationToken stoppingToken)
    {
        var bytes = new byte[1024];
        while (port.IsOpen && !stoppingToken.IsCancellationRequested)
        {
            var count = port.BytesToRead;
            if (count > 0)
            {
                count = port.Read(bytes, 0, Math.Min(bytes.Length, count));
                if (count > 0)
                {
                    _lastSerialByteAt = DateTime.UtcNow;
                    if (_lastSerialByteAt - _lastRawNoticeAt >= TimeSpan.FromSeconds(5))
                    {
                        _lastRawNoticeAt = _lastSerialByteAt;
                        _logger.LogInformation("Received serial bytes from {ComPort}. Raw HEX: {RawHex}",
                            _cfg.ComPort, Convert.ToHexString(bytes, 0, count));
                    }
                    foreach (var frame in ExtractFrames(bytes.AsSpan(0, count)))
                    {
                        if (!TryParseWeight(frame, out var kg, out var reason))
                        {
                            _logger.LogWarning(
                                "Ignored serial frame from {ComPort}: {Reason}. Raw HEX: {RawHex}",
                                _cfg.ComPort, reason, Convert.ToHexString(frame));
                            continue;
                        }

                        var now = DateTime.UtcNow;
                        if (kg != _lastWeight) { _lastWeight = kg; _lastChangedAt = now; }
                        var stable = now - _lastChangedAt >= TimeSpan.FromMilliseconds(Math.Max(0, _cfg.StableWeightDurationMs));
                        await SendAsync(kg, stable, "READING", null, stoppingToken);
                    }
                }
            }
            else
            {
                var now = DateTime.UtcNow;
                if (now - _lastSerialByteAt >= TimeSpan.FromSeconds(5)
                    && now - _lastNoDataNoticeAt >= TimeSpan.FromSeconds(15))
                {
                    _lastNoDataNoticeAt = now;
                    _logger.LogWarning(
                        "No serial bytes received from {ComPort} for {Seconds} seconds. Verify the indicator is powered and configured to transmit continuously, then verify baud/parity/data bits/stop bits.",
                        _cfg.ComPort, (int)(now - _lastSerialByteAt).TotalSeconds);
                    await SendAsync(0, false, "CONNECTED", null, stoppingToken);
                }
            }

            await Task.Delay(Math.Max(20, _cfg.ReadIntervalMs), stoppingToken);
        }
    }

    private IEnumerable<byte[]> ExtractFrames(ReadOnlySpan<byte> chunk)
    {
        _buffer.AddRange(chunk.ToArray());
        if (_buffer.Count > 4096) _buffer.RemoveRange(0, _buffer.Count - 4096);
        var start = HexByte(_cfg.StartByte) ?? 0x02;
        var end = HexByte(_cfg.EndByte) ?? 0x0A;
        var frames = new List<byte[]>();
        while (true)
        {
            var s = _buffer.IndexOf(start);
            if (s < 0) { _buffer.Clear(); break; }
            var e = _buffer.IndexOf(end, s + 1);
            if (e < 0) { if (s > 0) _buffer.RemoveRange(0, s); break; }
            frames.Add(_buffer.GetRange(s, e - s + 1).ToArray());
            _buffer.RemoveRange(0, e + 1);
        }
        return frames;
    }

    private bool TryParseWeight(byte[] frame, out decimal kg, out string reason)
    {
        kg = 0;
        reason = string.Empty;
        var start = Math.Max(0, _cfg.WeightStartPosition - 1);
        if (start + _cfg.WeightLength > frame.Length)
        {
            reason = $"WeightStartPosition {_cfg.WeightStartPosition} and WeightLength {_cfg.WeightLength} exceed frame length {frame.Length}";
            return false;
        }
        var text = Encoding.ASCII.GetString(frame, start, _cfg.WeightLength).Replace(' ', '0').Trim();
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out kg))
        {
            reason = $"weight field '{text}' is not numeric";
            return false;
        }
        if (_cfg.DecimalPlaces > 0 && !text.Contains('.')) kg /= (decimal)Math.Pow(10, _cfg.DecimalPlaces);
        var negative = HexByte(_cfg.NegativeSignHex);
        if (negative.HasValue && _cfg.SignPosition > 0 && _cfg.SignPosition <= frame.Length
            && frame[_cfg.SignPosition - 1] == negative.Value) kg = -kg;
        return true;
    }

    private async Task SendAsync(decimal kg, bool stable, string state, string? error, CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;
        if (now < _nextServerSendAt) return;
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(new
            {
                deviceId = _cfg.DeviceId, weightKg = kg, stable, readerState = state, error
            }), Encoding.UTF8, "application/json");
            // This unattended LAN client does not have a user JWT. The server authenticates
            // every reading with the same secret configured as CANE_SCALE_AGENT_KEY.
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_cfg.ApiBaseUrl.TrimEnd('/')}/api/scale-bridge/reading")
            { Content = content };
            request.Headers.TryAddWithoutValidation("X-Cane-Scale-Key", _cfg.AgentKey);
            using var response = await _http.SendAsync(request, stoppingToken);
            if (response.IsSuccessStatusCode)
            {
                // A display does not need every serial frame. This keeps the LAN/API responsive
                // and remains well below the server's anti-abuse request limit.
                _nextServerSendAt = now.AddMilliseconds(Math.Max(250, _cfg.ReadIntervalMs));
                return;
            }

            var body = await response.Content.ReadAsStringAsync();
            var retryAfter = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                ? TimeSpan.FromSeconds(5)
                : TimeSpan.FromSeconds(3);
            _nextServerSendAt = now.Add(retryAfter);
            _logger.LogWarning("Server rejected reading ({StatusCode}): {Response}. Retrying after {RetrySeconds} seconds.",
                (int)response.StatusCode, body, retryAfter.TotalSeconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _nextServerSendAt = now.AddSeconds(3);
            _logger.LogWarning(ex, "Server send failed. Retrying after 3 seconds.");
        }
    }

    private static Parity ParseParity(string? value) => Enum.TryParse<Parity>(value, true, out var parity) ? parity : Parity.None;
    private static byte? HexByte(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return byte.TryParse(value.Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) ? b : null;
    }
}

sealed class ScaleBridgeWorker : BackgroundService
{
    private readonly ScaleBridge _bridge;
    private readonly IOptions<ScaleBridgeOptions> _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ScaleBridgeWorker> _logger;

    public ScaleBridgeWorker(ScaleBridge bridge, IOptions<ScaleBridgeOptions> options,
        IHostApplicationLifetime lifetime, ILogger<ScaleBridgeWorker> logger) =>
        (_bridge, _options, _lifetime, _logger) = (bridge, options, lifetime, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl) || string.IsNullOrWhiteSpace(options.AgentKey)
            || options.AgentKey.StartsWith("SET_", StringComparison.OrdinalIgnoreCase) || options.DeviceId <= 0)
        {
            _logger.LogCritical("Set ScaleBridge.ApiBaseUrl, AgentKey and DeviceId in appsettings.json before starting Scale Bridge.");
            _lifetime.StopApplication();
            return;
        }

        _logger.LogInformation("CaneFactory Scale Bridge starting on {ComPort}; sending to {ApiBaseUrl} for device #{DeviceId}.",
            options.ComPort, options.ApiBaseUrl, options.DeviceId);
        await _bridge.RunAsync(stoppingToken);
    }
}

sealed class ScaleBridgeOptions
{
    public string ApiBaseUrl { get; set; } = "";
    public string AgentKey { get; set; } = "";
    public int DeviceId { get; set; }
    public string ComPort { get; set; } = "COM1";
    public int BaudRate { get; set; } = 2400;
    public string Parity { get; set; } = "None";
    public int DataBits { get; set; } = 8;
    public int StopBits { get; set; } = 1;
    public int ReadTimeoutMs { get; set; } = 1000;
    public int ReadIntervalMs { get; set; } = 500;
    public string StartByte { get; set; } = "02";
    public string EndByte { get; set; } = "0A";
    public int WeightStartPosition { get; set; } = 3;
    public int WeightLength { get; set; } = 6;
    public int SignPosition { get; set; } = 2;
    public string NegativeSignHex { get; set; } = "2D";
    public int DecimalPlaces { get; set; }
    public int StableWeightDurationMs { get; set; } = 2000;
}
