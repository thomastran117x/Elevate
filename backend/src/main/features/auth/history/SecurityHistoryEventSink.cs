using backend.main.features.auth.events;
using backend.main.shared.providers.messages;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.history;

/// <summary>
/// Queues the events the account owner should see for the security history. A background writer
/// stores them in the security history outbox, Debezium relays the outbox to the security-history
/// topic, and the security-history worker persists each entry and sends any alert.
/// </summary>
/// <remarks>
/// Sinks run inline on the request, so this one only enqueues: it never waits for the database or
/// Kafka and can never fail, slow, or roll back the security action that produced the event. Once
/// the outbox row is committed the entry survives a broker outage or a restart. Until then it is
/// held in memory; an entry the full queue refuses is counted on
/// <c>auth.security_history.dropped</c>.
/// </remarks>
public sealed class SecurityHistoryEventSink : IAuthSecurityEventSink
{
    private static readonly EventId DroppedEvent = new(33902, "SecurityHistoryEntryDropped");

    private readonly SecurityHistoryQueue _queue;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptionsMonitor<SecurityHistoryOptions> _options;
    private readonly AuthSecurityMetrics _metrics;
    private readonly ILogger<SecurityHistoryEventSink> _logger;

    public SecurityHistoryEventSink(
        SecurityHistoryQueue queue,
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<SecurityHistoryOptions> options,
        AuthSecurityMetrics metrics,
        ILogger<SecurityHistoryEventSink> logger)
    {
        _queue = queue;
        _httpContextAccessor = httpContextAccessor;
        _options = options;
        _metrics = metrics;
        _logger = logger;
    }

    public void Write(AuthSecurityEvent securityEvent)
    {
        var options = _options.CurrentValue;
        if (!options.PublishEnabled)
            return;

        var message = CreateMessage(securityEvent, options.IncludeMaskedNetwork);
        if (message is null)
            return;

        if (_queue.TryEnqueue(message))
            return;

        _metrics.RecordSecurityHistoryDropped();
        _logger.LogWarning(
            DroppedEvent,
            "Security history queue is full; the {Kind} entry was not recorded.",
            AuthSecurityWireNames.Of(message.Kind));
    }

    internal SecurityHistoryMessage? CreateMessage(AuthSecurityEvent securityEvent, bool includeNetwork)
    {
        if (SecurityHistoryCatalog.Classify(securityEvent) is not { } kind)
            return null;

        // An administrator's action runs on the administrator's request, so its browser, device and
        // network describe them, not the owner. The owner sees only that an administrator acted.
        var initiatedByAdmin = SecurityHistoryCatalog.IsInitiatedByAdmin(securityEvent);
        var (userAgent, network) = initiatedByAdmin ? (null, null) : ReadRequest(includeNetwork);

        return new SecurityHistoryMessage
        {
            MessageId = Guid.NewGuid(),
            UserId = securityEvent.UserId!.Value,
            Kind = kind,
            EventName = securityEvent.Definition.Name,
            Method = AuthSecurityWireNames.Of(securityEvent.Method),
            Factor = AuthSecurityWireNames.Of(securityEvent.Factor),
            Reason = SecurityHistoryCatalog.ReasonFor(securityEvent),
            Client = SecurityHistoryClients.FromUserAgent(userAgent),
            DeviceClass = initiatedByAdmin ? AuthDeviceClass.Unknown : securityEvent.DeviceClass,
            Network = network,
            CorrelationId = securityEvent.CorrelationId,
            InitiatedByAdmin = initiatedByAdmin,
            OccurredAt = securityEvent.OccurredAt,
        };
    }

    private (string? UserAgent, string? Network) ReadRequest(bool includeNetwork)
    {
        try
        {
            var context = _httpContextAccessor.HttpContext;
            if (context is null)
                return (null, null);

            return (
                context.Request.Headers.UserAgent.ToString(),
                includeNetwork ? NetworkAddressMasker.Mask(context.Connection.RemoteIpAddress) : null);
        }
        catch (ObjectDisposedException)
        {
            // The request already ended; the entry is still worth keeping without its context.
            return (null, null);
        }
    }
}
