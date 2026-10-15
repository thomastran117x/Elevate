using Microsoft.Extensions.Options;

namespace backend.main.features.auth.events;

/// <summary>
/// Writes each event as one structured log entry carrying the catalog <see cref="EventId"/>.
/// </summary>
/// <remarks>
/// The message template names every field, so the console output shows them even though its
/// template omits structured properties, and a structured sink still receives them as properties.
/// </remarks>
public sealed class LoggingAuthSecurityEventSink : IAuthSecurityEventSink
{
    private const string Template =
        "Auth security event {EventName} outcome={Outcome} reason={Reason} method={Method} "
        + "flow={Flow} dimension={Dimension} factor={Factor} change={Change} user={UserId} "
        + "actor={ActorUserId} session={SessionId} target={TargetHash} source={SourceIpHash} "
        + "client={ClientClass} device={DeviceClass} route={Route} correlation={CorrelationId}";

    private readonly ILogger<LoggingAuthSecurityEventSink> _logger;
    private readonly IOptionsMonitor<AuthSecurityEventsOptions> _options;

    public LoggingAuthSecurityEventSink(
        ILogger<LoggingAuthSecurityEventSink> logger,
        IOptionsMonitor<AuthSecurityEventsOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public void Write(AuthSecurityEvent securityEvent)
    {
        var definition = securityEvent.Definition;
        if (!_options.CurrentValue.LogSinkEnabled || !_logger.IsEnabled(definition.Level))
            return;

#pragma warning disable CA2254 // The template is a constant; only the level varies per event.
        _logger.Log(
            definition.Level,
            definition.EventId,
            Template,
            definition.Name,
            AuthSecurityWireNames.Of(securityEvent.Outcome),
            AuthSecurityWireNames.Of(securityEvent.Reason),
            AuthSecurityWireNames.Of(securityEvent.Method),
            AuthSecurityWireNames.Of(securityEvent.Flow),
            AuthSecurityWireNames.Of(securityEvent.Dimension),
            AuthSecurityWireNames.Of(securityEvent.Factor),
            AuthSecurityWireNames.Of(securityEvent.Change),
            securityEvent.UserId,
            securityEvent.ActorUserId,
            securityEvent.SessionId,
            securityEvent.TargetHash,
            securityEvent.SourceIpHash,
            AuthSecurityWireNames.Of(securityEvent.ClientClass),
            AuthSecurityWireNames.Of(securityEvent.DeviceClass),
            securityEvent.Route,
            securityEvent.CorrelationId);
#pragma warning restore CA2254
    }
}

/// <summary>Counts each event on <see cref="AuthSecurityMetrics"/>.</summary>
public sealed class MetricsAuthSecurityEventSink : IAuthSecurityEventSink
{
    private readonly AuthSecurityMetrics _metrics;
    private readonly IOptionsMonitor<AuthSecurityEventsOptions> _options;

    public MetricsAuthSecurityEventSink(
        AuthSecurityMetrics metrics,
        IOptionsMonitor<AuthSecurityEventsOptions> options)
    {
        _metrics = metrics;
        _options = options;
    }

    public void Write(AuthSecurityEvent securityEvent)
    {
        if (_options.CurrentValue.MetricsSinkEnabled)
            _metrics.RecordEvent(securityEvent);
    }
}
