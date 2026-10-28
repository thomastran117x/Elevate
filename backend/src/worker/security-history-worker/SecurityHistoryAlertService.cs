using backend.main.shared.utilities.logger;

namespace backend.worker.security_history_worker;

/// <summary>
/// Runs <see cref="SecurityHistoryAlertRunner"/> on a short poll, separately from the consumer, so
/// a slow or unavailable email topic delays alerts but never the history itself.
/// </summary>
public sealed class SecurityHistoryAlertService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SecurityHistoryWorkerOptions _options;

    public SecurityHistoryAlertService(IServiceScopeFactory scopeFactory, SecurityHistoryWorkerOptions options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.NotificationsEnabled)
        {
            Logger.Info("Security history alerts are off; the alert dispatcher is idle.");
            return;
        }

        if (!await SecurityHistorySchema.WaitUntilReadyAsync(_scopeFactory, PollInterval, nameof(SecurityHistoryAlertService), stoppingToken))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<SecurityHistoryAlertRunner>();
                handled = await runner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "[SecurityHistoryAlertService] Failed to dispatch security alerts.");
            }

            // A full batch means more are waiting; otherwise wait for the next poll.
            if (handled >= SecurityHistoryAlertRunner.BatchSize)
                continue;

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
