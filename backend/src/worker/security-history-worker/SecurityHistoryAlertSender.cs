using backend.main.features.auth.notifications;
using backend.main.infrastructure.database.core;
using backend.main.shared.providers.messages;
using backend.main.shared.utilities.logger;

using Microsoft.EntityFrameworkCore;

namespace backend.worker.security_history_worker;

/// <summary>
/// Emails the account owner about a newly recorded high-risk change.
/// </summary>
/// <remarks>
/// Best effort by design. The change already happened and the entry is already stored, so a
/// failure here is logged and counted, never retried into a duplicate history row, and never
/// passed back to the consumer, where it would hold up the queue behind it.
/// </remarks>
public sealed class SecurityHistoryAlertSender
{
    private readonly AppDatabaseContext _db;
    private readonly IAuthNotificationService _notifications;
    private readonly SecurityHistoryWorkerOptions _options;
    private readonly SecurityHistoryWorkerMetrics _metrics;

    public SecurityHistoryAlertSender(
        AppDatabaseContext db,
        IAuthNotificationService notifications,
        SecurityHistoryWorkerOptions options,
        SecurityHistoryWorkerMetrics metrics)
    {
        _db = db;
        _notifications = notifications;
        _options = options;
        _metrics = metrics;
    }

    public bool IsDue(SecurityHistoryMessage entry) =>
        _options.NotificationsEnabled && _options.AlertKinds.Contains(entry.Kind);

    public async Task SendIfDueAsync(SecurityHistoryMessage entry, CancellationToken cancellationToken = default)
    {
        if (!IsDue(entry))
            return;

        try
        {
            // Read here rather than carried on the message, so no address ever travels with it.
            var recipient = await _db.Users
                .AsNoTracking()
                .Where(user => user.Id == entry.UserId)
                .Select(user => new { user.Email, user.Name })
                .FirstOrDefaultAsync(cancellationToken);
            if (recipient is null || string.IsNullOrWhiteSpace(recipient.Email))
            {
                _metrics.RecordAlert(SecurityHistoryAlertOutcome.NoRecipient);
                return;
            }

            await _notifications.SendSecurityAlertAsync(recipient.Email, entry, recipient.Name);
            _metrics.RecordAlert(SecurityHistoryAlertOutcome.Sent);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _metrics.RecordAlert(SecurityHistoryAlertOutcome.Failed);
            Logger.Warn($"[SecurityHistoryAlertSender] Alert for entry {entry.MessageId} failed with {ex.GetType().Name}.");
        }
    }
}
