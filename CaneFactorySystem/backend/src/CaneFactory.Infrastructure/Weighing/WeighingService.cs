using System.IO.Ports;
using System.Text;
using CaneFactory.Application.Common;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CaneFactory.Infrastructure.Weighing;

/// <summary>
/// Singleton live-weight engine. Reads RS232/USB-serial frames using the ACTIVE WeighingDevice +
/// active StringProfile, parses them with the configured parser, tracks stability and broadcasts
/// via SignalR. Also provides a simulator mode for environments without physical hardware.
/// Runs fully on the local factory LAN - no internet required.
/// </summary>
public class WeighingService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILiveWeightBroadcaster _broadcaster;
    private readonly ILogger<WeighingService> _log;
    private readonly object _lock = new();

    private SerialPort? _port;
    private CancellationTokenSource? _readCts;
    private CancellationTokenSource? _simCts;
    private readonly List<byte> _buffer = new();
    private IWeightFrameParser? _parser;
    private StringProfile? _profile;
    private WeighingDevice? _device;

    private decimal _lastWeightKg;
    private DateTime _lastChangeAt = DateTime.UtcNow;
    private LiveWeightDto _current = new() { DeviceConnected = false, ReaderState = "DISCONNECTED" };
    public bool SimulatorRunning => _simCts != null;
    public bool Reading => _readCts != null;
    public bool Connected => _port?.IsOpen == true;
    public bool IsOperatingDevice(int deviceId) => _device?.Id == deviceId && (_port?.IsOpen == true || Reading);
    public int? OperatingDeviceId => _device?.Id;

    public bool TryGetUsableWeight(out decimal weightKg, out string reason)
    {
        var current = Current;
        var fresh = current.LastReceivedAt != default && DateTime.UtcNow - current.LastReceivedAt <= TimeSpan.FromSeconds(5);
        if (!current.DeviceConnected || !current.ReaderRunning || !current.IsLive || !fresh)
        {
            weightKg = 0;
            reason = current.ReaderState == "STOPPED"
                ? "Reading is stopped. Start the weighing device before saving."
                : "No fresh live weight is available. Connect and start the weighing device before saving.";
            return false;
        }
        weightKg = current.WeightKg;
        reason = string.Empty;
        return true;
    }

    public WeighingService(IServiceScopeFactory scopes, ILiveWeightBroadcaster broadcaster, ILogger<WeighingService> log)
    {
        _scopes = scopes;
        _broadcaster = broadcaster;
        _log = log;
    }

    public LiveWeightDto Current { get { lock (_lock) return _current; } }

    public static string[] AvailablePorts()
    {
        try { return SerialPort.GetPortNames(); }
        catch { return Array.Empty<string>(); }
    }

    public async Task<(bool ok, string message)> ConnectAsync(int deviceId)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var device = await db.WeighingDevices.Include(d => d.ActiveStringProfile)
            .FirstOrDefaultAsync(d => d.Id == deviceId && !d.IsDeleted);
        if (device == null) return (false, "Weighing device not found.");
        if (!device.IsEnabled) return (false, "Weighing device is disabled.");
        if (device.ActiveStringProfile == null) return (false, "No active String Profile is assigned to this device.");

        Disconnect();
        _device = device;
        _profile = device.ActiveStringProfile;
        _parser = ParserFactory.Create(_profile);

        try
        {
            var port = new SerialPort(device.ComPort, device.BaudRate,
                Enum.TryParse<Parity>(device.Parity, true, out var p) ? p : Parity.None,
                device.DataBits,
                device.StopBits == 2 ? System.IO.Ports.StopBits.Two : System.IO.Ports.StopBits.One)
            {
                ReadTimeout = device.ReadTimeoutMs,
                Handshake = device.FlowControl switch
                {
                    "XOnXOff" => Handshake.XOnXOff,
                    "RTS" => Handshake.RequestToSend,
                    _ => Handshake.None
                }
            };
            port.Open();
            _port = port;
            UpdateState(d => { d.DeviceConnected = true; d.DeviceName = device.DeviceName; d.Error = null; d.ReaderRunning = false; d.IsLive = false; d.ReaderState = "CONNECTED"; d.WeightKg = 0; d.WeightQuintal = 0; d.LastReceivedAt = default; });
            return (true, $"Connected to {device.ComPort} @ {device.BaudRate} baud.");
        }
        catch (Exception ex)
        {
            ClearLiveState("DISCONNECTED", ex.Message);
            return (false, $"Serial connection failed: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        StopReading();
        StopSimulator();
        try { _port?.Close(); _port?.Dispose(); } catch { /* ignore */ }
        _port = null;
        ClearLiveState("DISCONNECTED");
    }

    public (bool ok, string message) StartReading()
    {
        if (_port == null || !_port.IsOpen) return (false, "Device is not connected.");
        if (_readCts != null) return (true, "Already reading.");
        _readCts = new CancellationTokenSource();
        UpdateState(d => { d.ReaderRunning = true; d.IsLive = false; d.ReaderState = "READING"; d.WeightKg = 0; d.WeightQuintal = 0; d.LastReceivedAt = default; d.Error = null; });
        var interval = Math.Max(20, _device?.ReadIntervalMs ?? 200);
        _ = Task.Run(() => ReadLoopAsync(interval, _readCts.Token));
        return (true, "Reading started.");
    }

    public void StopReading()
    {
        _readCts?.Cancel();
        _readCts = null;
        if (_simCts == null) ClearLiveState(_port is { IsOpen: true } ? "STOPPED" : "DISCONNECTED");
    }

    private async Task ReadLoopAsync(int intervalMs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var port = _port;
                if (port == null || !port.IsOpen) break;
                var count = port.BytesToRead;
                if (count > 0)
                {
                    var bytes = new byte[count];
                    port.Read(bytes, 0, count);
                    foreach (var frame in ExtractFrames(bytes)) HandleFrame(frame);
                }
            }
            catch (Exception ex)
            {
                // A serial failure invalidates the measurement immediately. Recovery is deliberately
                // owned by WeighingRecoveryService so an explicit Stop/Disconnect cannot be undone
                // by a stale reader task retrying in the background.
                if (!ct.IsCancellationRequested)
                    _log.LogWarning(ex, "Serial read error; device marked disconnected");
                ClosePortAfterFailure(ex.Message);
                break;
            }
            await Task.Delay(intervalMs, ct).ContinueWith(_ => { });
        }
    }

    private void ClosePortAfterFailure(string error)
    {
        try { _port?.Close(); _port?.Dispose(); } catch { /* port is already unavailable */ }
        _port = null;
        _readCts = null;
        ClearLiveState("DISCONNECTED", error);
    }

    /// <summary>Assemble frames from the byte stream using configured start/end bytes (or LF fallback). Malformed/noisy data is safely skipped.</summary>
    private IEnumerable<byte[]> ExtractFrames(byte[] chunk)
    {
        var frames = new List<byte[]>();
        lock (_buffer)
        {
            _buffer.AddRange(chunk);
            if (_buffer.Count > 4096) _buffer.RemoveRange(0, _buffer.Count - 4096);
            var start = ParserFactory.HexByte(_profile?.StartByte);
            var end = ParserFactory.HexByte(_profile?.EndByte) ?? 0x0A;

            while (true)
            {
                int s = 0;
                if (start.HasValue)
                {
                    s = _buffer.IndexOf(start.Value);
                    if (s < 0) { _buffer.Clear(); break; }
                }
                var e = _buffer.IndexOf(end, s + 1);
                if (e < 0)
                {
                    if (s > 0) _buffer.RemoveRange(0, s);
                    break;
                }
                var frame = _buffer.GetRange(s, e - s + 1).ToArray();
                _buffer.RemoveRange(0, e + 1);
                frames.Add(frame);
            }
        }
        return frames;
    }

    public void HandleFrame(byte[] frame)
    {
        if (_parser == null || _profile == null) return;
        var parsed = _parser.Parse(frame);
        if (!parsed.FrameValid || parsed.NumericWeight == null || (!Reading && !SimulatorRunning)) return; // invalid/stopped frames ignored safely

        var kg = parsed.NumericWeight.Value;
        var now = DateTime.UtcNow;
        bool stable;
        lock (_lock)
        {
            if (kg != _lastWeightKg) { _lastWeightKg = kg; _lastChangeAt = now; }
            stable = (now - _lastChangeAt).TotalMilliseconds >= _profile.StableWeightDurationMs;
        }
        UpdateState(d =>
        {
            d.WeightKg = kg;
            d.WeightQuintal = WeightCalculator.KgToQuintal(kg);
            d.WeightUnit = _profile.WeightUnit;
            d.Stable = stable;
            d.LastReceivedAt = now;
            d.ReaderRunning = Reading || SimulatorRunning;
            d.IsLive = true;
            d.ReaderState = "READING";
            d.Error = null;
        });
        _ = _broadcaster.BroadcastAsync(Current);
    }

    // ---------- SIMULATOR (development / demo without physical indicator) ----------
    public (bool ok, string message) StartSimulator(StringProfile profile, decimal targetKg)
    {
        StopSimulator();
        _profile = profile;
        _parser = ParserFactory.Create(profile);
        UpdateState(d => { d.DeviceConnected = true; d.DeviceName = "SIMULATOR"; d.ReaderRunning = true; d.IsLive = false; d.ReaderState = "READING"; });
        _simCts = new CancellationTokenSource();
        var ct = _simCts.Token;
        _ = Task.Run(async () =>
        {
            var current = 0m;
            var rnd = new Random();
            while (!ct.IsCancellationRequested)
            {
                current = Math.Abs(targetKg - current) < 50 ? targetKg + rnd.Next(-2, 3) : current + Math.Sign(targetKg - current) * rnd.Next(200, 600);
                HandleFrame(BuildType15Frame(current));
                await Task.Delay(500, ct).ContinueWith(_ => { });
            }
        });
        return (true, "Simulator started.");
    }

    public void SetSimulatorWeight(decimal kg)
    {
        if (_profile != null) HandleFrame(BuildType15Frame(kg));
    }

    public void StopSimulator()
    {
        _simCts?.Cancel();
        _simCts = null;
        if (!Reading) ClearLiveState(_port is { IsOpen: true } ? "STOPPED" : "DISCONNECTED");
    }

    public static byte[] BuildType15Frame(decimal kg)
    {
        var sign = kg < 0 ? (byte)0x2D : (byte)0x20;
        var chars = Encoding.ASCII.GetBytes(Math.Abs((long)kg).ToString("D6"));
        var frame = new byte[11];
        frame[0] = 0x02;
        frame[1] = sign;
        Array.Copy(chars, 0, frame, 2, 6);
        frame[8] = 0x03;
        frame[9] = 0x0D;
        frame[10] = 0x0A;
        return frame;
    }

    private void UpdateState(Action<LiveWeightDto> mutate)
    {
        lock (_lock)
        {
            var copy = new LiveWeightDto
            {
                WeightKg = _current.WeightKg,
                WeightQuintal = _current.WeightQuintal,
                WeightUnit = _current.WeightUnit,
                Stable = _current.Stable,
                DeviceConnected = _current.DeviceConnected,
                ReaderRunning = _current.ReaderRunning,
                IsLive = _current.IsLive,
                ReaderState = _current.ReaderState,
                LastReceivedAt = _current.LastReceivedAt,
                DeviceName = _current.DeviceName,
                Error = _current.Error
            };
            mutate(copy);
            _current = copy;
        }
    }

    private void ClearLiveState(string state, string? error = null) => UpdateState(d =>
    {
        d.WeightKg = 0;
        d.WeightQuintal = 0;
        d.Stable = false;
        d.IsLive = false;
        d.ReaderRunning = false;
        d.ReaderState = state;
        d.LastReceivedAt = default;
        d.Error = error;
    });
}
