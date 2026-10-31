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
/// skips it. A send is bounded by <see cref="SendTimeout"/> and the host's cancellation. A send that
/// fails backs off exponentially and is marked <see cref="SecurityAlertState.Failed"/> after
/// <see cref="MaxAttempts"/>; one that times out may still be delivered, so it is marked
/// <see cref="SecurityAlertState.Unconfirmed"/> rather than retried. An alert older than
/// <see cref="SecurityHistoryWorkerOptions.AlertMaxAge"/> is <see cref="SecurityAlertState.Skipped"/>.
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
    private readonly SecurityHistoryWorkerOptions _options;
    private readonly SecurityHistoryWorkerMetrics _metrics;
    private readonly TimeSpan _sendTimeout;

    public SecurityHistoryAlertRunner(
        AppDatabaseContext db,
        IAuthNotificationService notifications,
        TimeProvider timeProvider,
        SecurityHistoryWorkerOptions options,
        SecurityHistoryWorkerMetrics metrics)
        : this(db, notifications, timeProvider, options, metrics, SendTimeout)
    {
    }

    /// <summary>Lets tests reach the timeout without waiting it out.</summary>
    internal SecurityHistoryAlertRunner(
        AppDatabaseContext db,
        IAuthNotificationService notifications,
        TimeProvider timeProvider,
        SecurityHistoryWorkerOptions options,
        SecurityHistoryWorkerMetrics metrics,
        TimeSpan sendTimeout)
    {
        _db = db;
        _notifications = notifications;
        _timeProvider = timeProvider;
        _options = options;
        _metrics = metrics;
        _sendTimeout = sendTimeout;
    }

    /// <returns>How many due alerts this pass looked at.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var passStartedAt = Now();
        var due = await _db.AccountSecurityEvents
            .AsNoTracking()
            .Where(e => e.AlertState == SecurityAlertState.Pending && e.AlertNextAttemptAtUtc <= passStartedAt)
            .OrderBy(e => e.AlertNextAttemptAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var entry in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeliverAsync(entry, cancellationToken);
        }

        return due.Count;
    }

    private async Task DeliverAsync(AccountSecurityEvent entry, CancellationToken cancellationToken)
    {
        // Read per alert, not once per pass: earlier sends in the batch can take up to
        // SendTimeout each, and a lease or backoff computed from the pass's start would already
        // have run out, letting another replica claim and send the same alert.
        var claimedAt = Now();
        var claimed = await _db.AccountSecurityEvents
            .Where(e => e.Id == entry.Id
                && e.AlertState == SecurityAlertState.Pending
                && e.AlertNextAttemptAtUtc == entry.AlertNextAttemptAtUtc)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(e => e.AlertNextAttemptAtUtc, claimedAt + ClaimLease),
                cancellationToken);
        if (claimed == 0)
            return;

        // A backlog (a worker that was down, alerts turned back on) must not send the owner a
        // burst of stale emails about changes long past.
        if (entry.OccurredAtUtc < claimedAt - _options.AlertMaxAge)
        {
            await RecordAsync(entry.Id, SecurityAlertState.Skipped, entry.AlertAttempts, nextAttemptAt: null);
            _metrics.RecordAlert(SecurityHistoryAlertOutcome.Skipped);
            return;
        }

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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            timeout.CancelAfter(_sendTimeout);
            await _notifications.SendSecurityAlertAsync(recipient.Email, entry, recipient.Name, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: the claim lapses and the alert is tried again by the next pass.
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            // Only the wait was abandoned. The producer still holds the message and goes on
            // delivering it, so a retry could email the owner twice; the outcome is unknown, and
            // it is recorded as such rather than retried.
            await RecordAsync(entry.Id, SecurityAlertState.Unconfirmed, attempts, nextAttemptAt: null);
            _metrics.RecordAlert(SecurityHistoryAlertOutcome.Unconfirmed);
            Logger.Warn($"[SecurityHistoryAlertRunner] Alert for entry {entry.Id} was not confirmed within {_sendTimeout.TotalSeconds:0}s; it is not retried, to avoid a duplicate.");
            return;
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

            await RecordAsync(entry.Id, SecurityAlertState.Pending, attempts, Now() + RetryDelay(attempts));
            _metrics.RecordAlert(SecurityHistoryAlertOutcome.Retried);
            Logger.Warn($"[SecurityHistoryAlertRunner] Alert for entry {entry.Id} failed with {ex.GetType().Name}; attempt {attempts} of {MaxAttempts}.");
            return;
        }

        await RecordAsync(entry.Id, SecurityAlertState.Sent, attempts, nextAttemptAt: null);
        _metrics.RecordAlert(SecurityHistoryAlertOutcome.Sent);
    }

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;

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
