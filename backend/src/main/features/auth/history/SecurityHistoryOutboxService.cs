using backend.main.shared.utilities.logger;

namespace backend.main.features.auth.history;

/// <summary>
/// Writes queued history entries to the outbox as they arrive, and retries with backoff while the
/// database is unavailable. On shutdown it stores whatever is still queued before the host stops.
/// </summary>
public sealed class SecurityHistoryOutboxService : BackgroundService
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly SecurityHistoryQueue _queue;
    private readonly SecurityHistoryOutboxDrainer _drainer;

    public SecurityHistoryOutboxService(SecurityHistoryQueue queue, SecurityHistoryOutboxDrainer drainer)
    {
        _queue = queue;
        _drainer = drainer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = InitialRetryDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _drainer.DrainAllAsync(stoppingToken);
                retryDelay = InitialRetryDelay;
                await _queue.WaitToReadAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[SecurityHistoryOutboxService] Storing security history failed with {ex.GetType().Name}; retrying in {retryDelay.TotalSeconds:0}s.");
                try
                {
                    await Task.Delay(retryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // Entries queued in the last moments before shutdown would otherwise be lost with the
        // process. Bounded, so an unreachable database cannot hold the shutdown up.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ShutdownDrainTimeout);
        try
        {
            await _drainer.DrainAllAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[SecurityHistoryOutboxService] Could not store queued security history on shutdown ({ex.GetType().Name}).");
        }
    }
}
