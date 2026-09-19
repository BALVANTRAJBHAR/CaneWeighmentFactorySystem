using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CaneFactory.Infrastructure.Weighing;

/// <summary>Restores only an explicitly requested active reader after process restart.</summary>
public sealed class WeighingRecoveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly WeighingService _weighing;
    private readonly ILogger<WeighingRecoveryService> _log;
    private DateTime _lastFailureLogAt = DateTime.MinValue;
    private string? _lastFailureKey;

    public WeighingRecoveryService(IServiceScopeFactory scopes, WeighingService weighing, ILogger<WeighingRecoveryService> log)
        => (_scopes, _weighing, _log) = (scopes, weighing, log);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var desired = await db.WeighingDevices.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.ActiveConfiguration && d.IsEnabled
                        && d.DesiredConnectionState == "Connected" && d.DesiredReaderRunning,
                        stoppingToken);
                if (desired != null)
                {
                    // RemoteAgent means the USB/RS232 converter is connected to another LAN PC.
                    // That PC runs Scale Bridge; a server process cannot open its COM port.
                    if (string.Equals(desired.ConnectionType, "RemoteAgent", StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                        continue;
                    }
                    if (!_weighing.Connected)
                    {
                        var (ok, message) = await _weighing.ConnectAsync(desired.Id);
                        if (!ok) LogRestoreFailure(desired.DeviceName, message);
                    }
                    if (_weighing.Connected && desired.DesiredReaderRunning && !_weighing.Reading)
                    {
                        var (ok, message) = _weighing.StartReading();
                        if (!ok) LogRestoreFailure(desired.DeviceName, message);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Weighing recovery check failed"); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private void LogRestoreFailure(string deviceName, string message)
    {
        var key = $"{deviceName}:{message}";
        var now = DateTime.UtcNow;
        if (_lastFailureKey == key && now - _lastFailureLogAt < TimeSpan.FromMinutes(1)) return;
        _lastFailureKey = key;
        _lastFailureLogAt = now;
        _log.LogWarning("Unable to restore weighing device {Device}: {Message}. Retry remains enabled because the saved desired state requires it.", deviceName, message);
    }
}
