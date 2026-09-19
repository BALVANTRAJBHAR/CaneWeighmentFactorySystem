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
        while (port.IsOpen)
        {
            var count = await port.BaseStream.ReadAsync(bytes.AsMemory(), stoppingToken);
            if (count <= 0) continue;
            foreach (var frame in ExtractFrames(bytes.AsSpan(0, count)))
            {
                if (!TryParseWeight(frame, out var kg)) continue;
                var now = DateTime.UtcNow;
                if (kg != _lastWeight) { _lastWeight = kg; _lastChangedAt = now; }
                var stable = now - _lastChangedAt >= TimeSpan.FromMilliseconds(Math.Max(0, _cfg.StableWeightDurationMs));
                await SendAsync(kg, stable, "READING", null, stoppingToken);
            }
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

    private bool TryParseWeight(byte[] frame, out decimal kg)
    {
        kg = 0;
        var start = Math.Max(0, _cfg.WeightStartPosition - 1);
        if (start + _cfg.WeightLength > frame.Length) return false;
        var text = Encoding.ASCII.GetString(frame, start, _cfg.WeightLength).Replace(' ', '0').Trim();
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out kg)) return false;
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
            using var response = await _http.PostAsync($"{_cfg.ApiBaseUrl.TrimEnd('/')}/api/scale-bridge/reading", content, stoppingToken);
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
