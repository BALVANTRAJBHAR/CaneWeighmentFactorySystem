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
                        && d.DesiredConnectionState == "Connected" && d.DesiredReaderRunning, stoppingToken);
                if (desired != null)
                {
                    if (!_weighing.Connected)
                    {
                        var (ok, message) = await _weighing.ConnectAsync(desired.Id);
                        if (!ok) _log.LogWarning("Unable to restore weighing device {Device}: {Message}", desired.DeviceName, message);
                    }
                    if (_weighing.Connected && !_weighing.Reading) _weighing.StartReading();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Weighing recovery check failed"); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
