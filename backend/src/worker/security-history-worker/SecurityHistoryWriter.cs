using backend.main.features.auth.history;
using backend.main.infrastructure.database.core;
using backend.main.shared.providers.messages;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace backend.worker.security_history_worker;

public enum SecurityHistoryWriteResult
{
    Inserted,
    /// <summary>A redelivery of a message already stored.</summary>
    Duplicate,
    /// <summary>The account was deleted before the entry arrived; its history went with it.</summary>
    AccountDeleted,
    /// <summary>The entry was already past its retention window when it arrived.</summary>
    Expired,
}

/// <summary>
/// Appends one entry to the account security history. Never updates or deletes.
/// </summary>
public sealed class SecurityHistoryWriter
{
    private readonly AppDatabaseContext _db;
    private readonly SecurityHistoryWorkerOptions _options;
    private readonly TimeProvider _timeProvider;

    public SecurityHistoryWriter(
        AppDatabaseContext db,
        SecurityHistoryWorkerOptions options,
        TimeProvider timeProvider)
    {
        _db = db;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<SecurityHistoryWriteResult> InsertAsync(
        SecurityHistoryMessage message,
        CancellationToken cancellationToken = default)
    {
        // A retried attempt must not resubmit the entity a failed attempt left tracked.
        _db.ChangeTracker.Clear();

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var occurredAt = message.OccurredAt.UtcDateTime;
        // Fixed at write time, so a later change to the retention setting applies to new entries.
        var expiresAt = occurredAt.AddDays(_options.RetentionDays);
        if (expiresAt <= now)
            return SecurityHistoryWriteResult.Expired;

        var alertDue = _options.NotificationsEnabled && _options.AlertKinds.Contains(message.Kind);

        _db.AccountSecurityEvents.Add(new AccountSecurityEvent
        {
            MessageId = message.MessageId,
            UserId = message.UserId,
            Kind = message.Kind,
            EventName = message.EventName,
            Method = message.Method,
            Factor = message.Factor,
            Reason = message.Reason,
            Client = message.Client,
            DeviceClass = message.DeviceClass,
            Network = message.Network,
            CorrelationId = message.CorrelationId,
            InitiatedByAdmin = message.InitiatedByAdmin,
            OccurredAtUtc = occurredAt,
            RecordedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            // Decided here and stored in the same insert, so an alert owed for a committed entry
            // survives a crash or a redelivery; the alert dispatcher sends it from the row.
            AlertState = alertDue ? SecurityAlertState.Pending : SecurityAlertState.None,
            AlertNextAttemptAtUtc = alertDue ? now : null,
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return SecurityHistoryWriteResult.Inserted;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return SecurityHistoryWriteResult.Duplicate;
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return SecurityHistoryWriteResult.AccountDeleted;
        }
    }

    // SQLite, used by the unit tests, reports the same two constraints by extended error code.
    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode is 2067 or 1555,
        _ => false,
    };

    private static bool IsForeignKeyViolation(DbUpdateException ex) => ex.InnerException switch
    {
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.ForeignKeyViolation,
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode == 787,
        _ => false,
    };
}
