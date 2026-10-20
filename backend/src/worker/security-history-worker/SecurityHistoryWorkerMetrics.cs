using System.Diagnostics.Metrics;

using backend.main.features.auth.events;

namespace backend.worker.security_history_worker;

/// <summary>
/// Counters on the <c>EventXperience.SecurityHistory</c> meter. Tags come from closed sets only.
/// </summary>
public sealed class SecurityHistoryWorkerMetrics
{
    public const string MeterName = "EventXperience.SecurityHistory";

    private readonly Counter<long> _entries;
    private readonly Counter<long> _alerts;
    private readonly Counter<long> _purged;

    public SecurityHistoryWorkerMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _entries = meter.CreateCounter<long>(
            "security_history.entries",
            unit: "{entry}",
            description: "Security history messages processed, by outcome.");
        _alerts = meter.CreateCounter<long>(
            "security_history.alerts",
            unit: "{alert}",
            description: "Security alert emails handed to the email topic, by outcome.");
        _purged = meter.CreateCounter<long>(
            "security_history.purged",
            unit: "{entry}",
            description: "Security history entries deleted by the retention purge.");
    }

    public void RecordEntry(SecurityHistoryEntryOutcome outcome) =>
        _entries.Add(1, new KeyValuePair<string, object?>("outcome", AuthSecurityWireNames.Of(outcome)));

    public void RecordAlert(SecurityHistoryAlertOutcome outcome) =>
        _alerts.Add(1, new KeyValuePair<string, object?>("outcome", AuthSecurityWireNames.Of(outcome)));

    public void RecordPurged(int count)
    {
        if (count > 0)
            _purged.Add(count);
    }
}

public enum SecurityHistoryEntryOutcome
{
    Inserted,
    Duplicate,
    AccountDeleted,
    Expired,
    Rejected,
    Failed,
}

public enum SecurityHistoryAlertOutcome
{
    Sent,
    Failed,
    NoRecipient,
}
