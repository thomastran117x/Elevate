using backend.main.infrastructure.database.core;
using backend.main.shared.utilities.logger;

using Microsoft.EntityFrameworkCore;

namespace backend.worker.security_history_worker;

/// <summary>
/// Deletes security history entries past their <c>ExpiresAtUtc</c>.
/// <para>
/// Split from <see cref="SecurityHistoryRetentionService"/> so the work is a plain scoped class
/// that tests drive directly with a fake <see cref="TimeProvider"/>.
/// </para>
/// </summary>
public sealed class SecurityHistoryRetentionRunner
{
    private readonly AppDatabaseContext _db;
    private readonly SecurityHistoryWorkerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SecurityHistoryWorkerMetrics _metrics;

    public SecurityHistoryRetentionRunner(
        AppDatabaseContext db,
        SecurityHistoryWorkerOptions options,
        TimeProvider timeProvider,
        SecurityHistoryWorkerMetrics metrics)
    {
        _db = db;
        _options = options;
        _timeProvider = timeProvider;
        _metrics = metrics;
    }

    /// <returns>How many entries were deleted.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var purged = 0;

        // Bounded rather than "until empty": a large backlog should not hold one DbContext open
        // indefinitely. Whatever is left waits for the next pass.
        for (var batch = 0; batch < _options.PurgeMaxBatches; batch++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var ids = await _db.AccountSecurityEvents
                .AsNoTracking()
                .Where(e => e.ExpiresAtUtc <= now)
                .OrderBy(e => e.ExpiresAtUtc)
                .Take(_options.PurgeBatchSize)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0)
                break;

            purged += await _db.AccountSecurityEvents
                .Where(e => ids.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken);

            // A short batch means the backlog is drained; no point paying for another round trip.
            if (ids.Count < _options.PurgeBatchSize)
                break;
        }

        _metrics.RecordPurged(purged);
        if (purged > 0)
            Logger.Info($"[SecurityHistoryRetentionRunner] Purged {purged} security history entries past retention.");

        return purged;
    }
}
