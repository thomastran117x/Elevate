using backend.main.shared.utilities.logger;

namespace backend.worker.security_history_worker;

/// <summary>
/// Runs the retention purge on a timer.
/// <para>
/// Not leader-elected, so every replica purges. The deletes are idempotent, which makes that safe
/// if slightly wasteful, the same trade the API's cleanup services make. The read path hides
/// expired entries as well, so nothing is shown between expiry and the next pass.
/// </para>
/// </summary>
public sealed class SecurityHistoryRetentionService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly SecurityHistoryWorkerOptions _options;

    public SecurityHistoryRetentionService(IServiceProvider services, SecurityHistoryWorkerOptions options)
    {
        _services = services;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<SecurityHistoryRetentionRunner>();
                await runner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "[SecurityHistoryRetentionService] Failed to purge expired security history entries.");
            }

            try
            {
                await Task.Delay(_options.PurgeInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
