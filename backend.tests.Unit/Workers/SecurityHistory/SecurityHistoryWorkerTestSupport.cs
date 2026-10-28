using System.Diagnostics.Metrics;

using backend.main.features.auth.events;
using backend.main.features.auth.history;
using backend.main.shared.providers.messages;
using backend.tests.Unit.Features.Auth.Events;
using backend.worker.security_history_worker;

namespace backend.tests.Unit.Workers.SecurityHistory;

internal static class SecurityHistoryWorkerTestSupport
{
    public static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    public static SecurityHistoryWorkerOptions Options(
        int retentionDays = 90,
        int purgeBatchSize = 500,
        int purgeMaxBatches = 20,
        bool notificationsEnabled = true,
        IReadOnlySet<AccountSecurityEventKind>? alertKinds = null) => new(
            BootstrapServers: "kafka:9092",
            Topic: "eventxperience-security-history",
            GroupId: "security-history-worker",
            DlqTopic: "eventxperience-security-history-dlq",
            RetentionDays: retentionDays,
            PurgeBatchSize: purgeBatchSize,
            PurgeMaxBatches: purgeMaxBatches,
            PurgeInterval: TimeSpan.FromMinutes(60),
            NotificationsEnabled: notificationsEnabled,
            AlertKinds: alertKinds ?? SecurityHistoryWorkerOptions.DefaultAlertKinds);

    public static SecurityHistoryMessage Message(
        int userId = 1,
        AccountSecurityEventKind kind = AccountSecurityEventKind.PasswordChanged,
        DateTimeOffset? occurredAt = null,
        Guid? messageId = null) => new()
        {
            MessageId = messageId ?? Guid.NewGuid(),
            UserId = userId,
            Kind = kind,
            EventName = AuthSecurityEvents.FactorChanged.Name,
            Factor = "password",
            Client = SecurityHistoryClient.Chrome,
            DeviceClass = AuthDeviceClass.Desktop,
            Network = "203.0.113.0/24",
            CorrelationId = "0f8fad5b-d9cb-469f-a165-70867728950e",
            OccurredAt = occurredAt ?? Now.AddMinutes(-1),
        };

    public static (SecurityHistoryWorkerMetrics Metrics, MetricCollector Collector) Metrics()
    {
        var factory = AuthSecurityEventTestSupport.CreateMeterFactory();
        var collector = new MetricCollector(factory);
        return (new SecurityHistoryWorkerMetrics(factory), collector);
    }
}

/// <summary>Collects measurements from the worker's own meter.</summary>
internal sealed class MetricCollector : IDisposable
{
    private readonly MeterListener _listener = new();

    public MetricCollector(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(SecurityHistoryWorkerMetrics.MeterName);
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome")
                    outcome = tag.Value as string;
            }

            lock (Measurements)
                Measurements.Add((instrument.Name, value, outcome));
        });
        _listener.Start();
    }

    public List<(string Instrument, long Value, string? Outcome)> Measurements { get; } = [];

    public void Dispose() => _listener.Dispose();
}
