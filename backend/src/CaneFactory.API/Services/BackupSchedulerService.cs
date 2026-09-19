namespace CaneFactory.API.Services;

/// <summary>Bootstraps the in-process SQL Express backup scheduler as soon as the API starts.</summary>
public sealed class BackupSchedulerService : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILoggerFactory _loggerFactory;

    public BackupSchedulerService(IServiceScopeFactory scopes, ILoggerFactory loggerFactory)
        => (_scopes, _loggerFactory) = (scopes, loggerFactory);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        BackupScheduleRuntime.EnsureStarted(_scopes, _loggerFactory);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
