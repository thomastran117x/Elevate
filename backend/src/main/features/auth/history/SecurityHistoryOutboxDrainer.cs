using backend.main.features.auth.events;
using backend.main.shared.providers.messages;

namespace backend.main.features.auth.history;

/// <summary>
/// Moves queued history entries into the outbox, one batch at a time.
/// </summary>
/// <remarks>
/// A batch that fails to store is kept and offered again on the next call, so a database outage
/// delays entries rather than losing them; only the queue's capacity bounds how long it can wait.
/// Split from <see cref="SecurityHistoryOutboxService"/> so tests can drain on demand.
/// </remarks>
public sealed class SecurityHistoryOutboxDrainer
{
    public const int BatchSize = 100;

    private readonly SecurityHistoryQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AuthSecurityMetrics _metrics;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<SecurityHistoryMessage> _pending = [];

    public SecurityHistoryOutboxDrainer(
        SecurityHistoryQueue queue,
        IServiceScopeFactory scopeFactory,
        AuthSecurityMetrics metrics)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _metrics = metrics;
    }

    /// <returns>How many entries were stored; 0 when nothing was waiting.</returns>
    /// <exception cref="Exception">The outbox write failed; the batch is kept for the next call.</exception>
    public async Task<int> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var batch = _pending.Count > 0 ? _pending : _queue.TakeBatch(BatchSize);
            if (batch.Count == 0)
                return 0;

            _pending = batch;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider
                    .GetRequiredService<SecurityHistoryOutboxWriter>()
                    .WriteAsync(batch, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _metrics.RecordSecurityHistoryStoreFailure();
                throw;
            }

            _pending = [];
            return batch.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stores everything queued now, for shutdown and for tests.</summary>
    public async Task<int> DrainAllAsync(CancellationToken cancellationToken = default)
    {
        var total = 0;
        int stored;
        while ((stored = await DrainOnceAsync(cancellationToken)) > 0)
            total += stored;
        return total;
    }
}
