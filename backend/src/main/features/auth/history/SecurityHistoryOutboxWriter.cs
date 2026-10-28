using System.Text.Json;

using backend.main.infrastructure.database.core;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;

namespace backend.main.features.auth.history;

/// <summary>
/// Stores queued history entries as outbox rows. Once a row is committed the entry survives a
/// broker outage or a restart: Debezium relays it to the security-history topic whenever it can.
/// </summary>
public sealed class SecurityHistoryOutboxWriter
{
    public const string AggregateType = "security-history";
    public const string RecordType = "record";

    private readonly AppDatabaseContext _db;

    public SecurityHistoryOutboxWriter(AppDatabaseContext db) => _db = db;

    public async Task WriteAsync(
        IReadOnlyCollection<SecurityHistoryMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
            return;

        // A retried batch must not resubmit rows a failed attempt left tracked.
        _db.ChangeTracker.Clear();

        foreach (var message in messages)
        {
            _db.SecurityHistoryOutbox.Add(new SecurityHistoryOutbox
            {
                AggregateType = AggregateType,
                AggregateId = message.UserId.ToString(),
                Type = RecordType,
                // The same serialization the worker reads: camelCase, enums by name.
                Payload = JsonSerializer.Serialize(message, JsonOptions.Default),
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
