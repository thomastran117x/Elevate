using backend.main.application.environment;
using backend.main.features.auth.events;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.history;

/// <summary>
/// Hands the events the account owner should see to the security-history topic, where the
/// security-history worker persists them and sends any alert.
/// </summary>
/// <remarks>
/// Sinks run inline on the request, so this one never waits for Kafka: the publish is started and
/// left to finish on its own. A publish that fails is counted and logged by exception type only;
/// it can never fail, slow, or roll back the security action that produced the event. The cost of
/// that trade is that an event can be lost if the broker is unreachable, which the
/// <c>auth.security_history.publish_failures</c> counter makes visible.
/// </remarks>
public sealed class SecurityHistoryEventSink : IAuthSecurityEventSink
{
    private static readonly EventId PublishFailedEvent = new(33902, "SecurityHistoryPublishFailed");

    // Resolved on first publish, not when the emitter is built: constructing the Kafka producer
    // loads its native library, and every host that builds the emitter (OpenAPI export, tests)
    // would otherwise need it at startup even if it never records an event.
    private readonly Lazy<IPublisher> _publisher;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptionsMonitor<SecurityHistoryOptions> _options;
    private readonly AuthSecurityMetrics _metrics;
    private readonly ILogger<SecurityHistoryEventSink> _logger;
    private readonly string _topic;

    public SecurityHistoryEventSink(
        IServiceProvider services,
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<SecurityHistoryOptions> options,
        AuthSecurityMetrics metrics,
        ILogger<SecurityHistoryEventSink> logger)
        : this(
            new Lazy<IPublisher>(services.GetRequiredService<IPublisher>),
            httpContextAccessor,
            options,
            metrics,
            logger,
            EnvironmentSetting.SecurityHistoryTopic)
    {
    }

    internal SecurityHistoryEventSink(
        IPublisher publisher,
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<SecurityHistoryOptions> options,
        AuthSecurityMetrics metrics,
        ILogger<SecurityHistoryEventSink> logger,
        string topic)
        : this(new Lazy<IPublisher>(publisher), httpContextAccessor, options, metrics, logger, topic)
    {
    }

    private SecurityHistoryEventSink(
        Lazy<IPublisher> publisher,
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<SecurityHistoryOptions> options,
        AuthSecurityMetrics metrics,
        ILogger<SecurityHistoryEventSink> logger,
        string topic)
    {
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
        _options = options;
        _metrics = metrics;
        _logger = logger;
        _topic = topic;
    }

    public void Write(AuthSecurityEvent securityEvent)
    {
        var options = _options.CurrentValue;
        if (!options.PublishEnabled)
            return;

        var message = CreateMessage(securityEvent, options.IncludeMaskedNetwork);
        if (message is null)
            return;

        _ = PublishAsync(message);
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

    private async Task PublishAsync(SecurityHistoryMessage message)
    {
        try
        {
            await _publisher.Value.PublishAsync(_topic, message);
        }
        catch (Exception ex)
        {
            _metrics.RecordSecurityHistoryPublishFailure();

            // The exception's type only: a broker error can echo the payload it was handed.
            _logger.LogWarning(
                PublishFailedEvent,
                "Security history publish failed with {ExceptionType}; the entry is lost.",
                ex.GetType().Name);
        }
    }
}
