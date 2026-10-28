using backend.main.features.auth.history;
using backend.main.features.auth.notifications;
using backend.main.infrastructure.database.core;
using backend.main.shared.utilities.logger;

using Microsoft.EntityFrameworkCore;

namespace backend.worker.security_history_worker;

/// <summary>
/// Sends the alert emails owed by stored security history entries.
/// </summary>
/// <remarks>
/// <para>
/// The alert's state lives on the entry, written in the same insert, so an alert cannot be lost
/// once its entry is committed: a crash, a redelivery, or a failed publish leaves it
/// <see cref="SecurityAlertState.Pending"/> for a later pass.
/// </para>
/// <para>
/// Each pending alert is claimed first by moving its next attempt forward, so a second replica
/// skips it. A send is bounded by <see cref="SendTimeout"/> and the host's cancellation, failures
/// back off exponentially, and after <see cref="MaxAttempts"/> the alert is marked
/// <see cref="SecurityAlertState.Failed"/> rather than retried forever.
/// </para>
/// </remarks>
public sealed class SecurityHistoryAlertRunner
{
    public const int BatchSize = 20;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Longer than a send can take, so a claimed alert is not picked up again mid-send.</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(30);

    private readonly AppDatabaseContext _db;
    private readonly IAuthNotificationService _notifications;
    private readonly TimeProvider _timeProvider;
    private readonly SecurityHistoryWorkerMetrics _metrics;

    public SecurityHistoryAlertRunner(
        AppDatabaseContext db,
        IAuthNotificationService notifications,
        TimeProvider timeProvider,
        SecurityHistoryWorkerMetrics metrics)
    {
        _db = db;
        _notifications = notifications;
        _timeProvider = timeProvider;
        _metrics = metrics;
    }

    /// <returns>How many due alerts this pass looked at.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var due = await _db.AccountSecurityEvents
            .AsNoTracking()
            .Where(e => e.AlertState == SecurityAlertState.Pending && e.AlertNextAttemptAtUtc <= now)
            .OrderBy(e => e.AlertNextAttemptAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var entry in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeliverAsync(entry, now, cancellationToken);
        }

        return due.Count;
    }

    private async Task DeliverAsync(AccountSecurityEvent entry, DateTime now, CancellationToken cancellationToken)
    {
        var claimed = await _db.AccountSecurityEvents
            .Where(e => e.Id == entry.Id
                && e.AlertState == SecurityAlertState.Pending
                && e.AlertNextAttemptAtUtc == entry.AlertNextAttemptAtUtc)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(e => e.AlertNextAttemptAtUtc, now + ClaimLease),
                cancellationToken);
        if (claimed == 0)
            return;

        // Read when the alert is sent rather than carried on the message, so no address ever
        // travels with the entry.
        var recipient = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == entry.UserId)
            .Select(user => new { user.Email, user.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (recipient is null || string.IsNullOrWhiteSpace(recipient.Email))
        {
            await RecordAsync(entry.Id, SecurityAlertState.Failed, entry.AlertAttempts, nextAttemptAt: null);
            _metrics.RecordAlert(SecurityHistoryAlertOutcome.NoRecipient);
            return;
        }

        var attempts = entry.AlertAttempts + 1;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SendTimeout);
            await _notifications.SendSecurityAlertAsync(recipient.Email, entry, recipient.Name, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: the claim lapses and the alert is tried again by the next pass.
            throw;
        }
        catch (Exception ex)
        {
            if (attempts >= MaxAttempts)
            {
                await RecordAsync(entry.Id, SecurityAlertState.Failed, attempts, nextAttemptAt: null);
                _metrics.RecordAlert(SecurityHistoryAlertOutcome.Failed);
                Logger.Warn($"[SecurityHistoryAlertRunner] Gave up on the alert for entry {entry.Id} after {attempts} attempts ({ex.GetType().Name}).");
                return;
            }

            await RecordAsync(entry.Id, SecurityAlertState.Pending, attempts, now + RetryDelay(attempts));
            _metrics.RecordAlert(SecurityHistoryAlertOutcome.Retried);
            Logger.Warn($"[SecurityHistoryAlertRunner] Alert for entry {entry.Id} failed with {ex.GetType().Name}; attempt {attempts} of {MaxAttempts}.");
            return;
        }

        await RecordAsync(entry.Id, SecurityAlertState.Sent, attempts, nextAttemptAt: null);
        _metrics.RecordAlert(SecurityHistoryAlertOutcome.Sent);
    }

    // Not cancellable: once an email has been handed over, recording that fact matters more than a
    // prompt shutdown, or the next pass would send it again.
    private Task RecordAsync(long id, SecurityAlertState state, int attempts, DateTime? nextAttemptAt) =>
        _db.AccountSecurityEvents
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(e => e.AlertState, state)
                    .SetProperty(e => e.AlertAttempts, attempts)
                    .SetProperty(e => e.AlertNextAttemptAtUtc, nextAttemptAt),
                CancellationToken.None);

    internal static TimeSpan RetryDelay(int attempts)
    {
        var delay = FirstRetryDelay * Math.Pow(2, Math.Max(0, attempts - 1));
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }
}
