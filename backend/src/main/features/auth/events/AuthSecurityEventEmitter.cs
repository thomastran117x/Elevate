using System.Security.Claims;

using backend.main.application.handlers;
using backend.main.features.auth.token;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.events;

/// <summary>
/// A destination for authentication security events, such as logs, metrics, or the persisted
/// account security history.
/// </summary>
/// <remarks>
/// <see cref="Write"/> runs inline on the request that produced the event, so an implementation
/// must not block on I/O; one that persists should hand the event to a queue. A sink may throw:
/// the emitter isolates it, so a broken sink cannot fail authentication or starve other sinks.
/// </remarks>
public interface IAuthSecurityEventSink
{
    void Write(AuthSecurityEvent securityEvent);
}

/// <summary>
/// What auth code calls to record a security event. Never throws.
/// </summary>
public interface IAuthSecurityEventEmitter
{
    /// <param name="securityEvent">The classified event. Enrichment fields are overwritten.</param>
    /// <param name="target">The account identifier the event is aimed at when there is no account
    /// id; only its keyed hash is kept.</param>
    void Emit(AuthSecurityEvent securityEvent, AuthSecurityTarget? target = null);
}

public sealed class AuthSecurityEventEmitter : IAuthSecurityEventEmitter
{
    private static readonly EventId SinkFailedEvent = new(33901, "AuthSecurityEventSinkFailed");

    private readonly IReadOnlyList<IAuthSecurityEventSink> _sinks;
    private readonly AuthIdentifierHasher _hasher;
    private readonly AuthSecurityEventsOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly AuthSecurityMetrics _metrics;
    private readonly ILogger<AuthSecurityEventEmitter> _logger;

    public AuthSecurityEventEmitter(
        IEnumerable<IAuthSecurityEventSink> sinks,
        AuthIdentifierHasher hasher,
        IOptions<AuthSecurityEventsOptions> options,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        AuthSecurityMetrics metrics,
        ILogger<AuthSecurityEventEmitter> logger)
    {
        _sinks = sinks.ToList();
        _hasher = hasher;
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _logger = logger;
    }

    public void Emit(AuthSecurityEvent securityEvent, AuthSecurityTarget? target = null)
    {
        AuthSecurityEvent enriched;
        try
        {
            enriched = Enrich(securityEvent, target);
        }
        catch (Exception ex)
        {
            ReportFailure(nameof(AuthSecurityEventEmitter), ex);
            return;
        }

        foreach (var sink in _sinks)
        {
            try
            {
                sink.Write(enriched);
            }
            catch (Exception ex)
            {
                ReportFailure(sink.GetType().Name, ex);
            }
        }
    }

    internal AuthSecurityEvent Enrich(AuthSecurityEvent securityEvent, AuthSecurityTarget? target)
    {
        var context = _httpContextAccessor.HttpContext;
        var userAgent = context?.Request.Headers.UserAgent.ToString() ?? string.Empty;
        var principal = context?.User;
        var principalUserId = int.TryParse(
            principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            out var parsedUserId)
            ? parsedUserId
            : (int?)null;
        var userId = securityEvent.UserId ?? principalUserId;

        // The caller's session is only borrowed for an event about the caller's own account.
        var sessionId = NormalizeSessionId(securityEvent.SessionId)
            ?? (userId is not null && userId == principalUserId
                ? NormalizeSessionId(principal?.FindFirst(TokenService.SessionIdClaimType)?.Value)
                : null);

        return securityEvent with
        {
            UserId = userId,
            SessionId = sessionId,
            TargetHash = _options.IncludeTargetHash && target is { } aimedAt
                ? _hasher.HashTarget(aimedAt)
                : null,
            SourceIpHash = _options.IncludeSourceIpHash && context is not null
                ? _hasher.HashSourceIp(context.Connection.RemoteIpAddress?.ToString())
                : null,
            ClientClass = ClassifyClient(userAgent),
            DeviceClass = ClassifyDevice(userAgent),
            CorrelationId = RequestIdHandler.IsValidRequestId(context?.TraceIdentifier)
                ? context!.TraceIdentifier
                : null,
            OccurredAt = _timeProvider.GetUtcNow(),
        };
    }

    private void ReportFailure(string sink, Exception ex)
    {
        _metrics.RecordSinkFailure(sink);

        // The exception's type only: its message could carry whatever the sink was handling.
        _logger.LogWarning(
            SinkFailedEvent,
            "Auth security event sink {Sink} failed with {ExceptionType}.",
            sink,
            ex.GetType().Name);
    }

    private static string? NormalizeSessionId(string? sessionId) =>
        Guid.TryParse(sessionId, out var parsed) ? parsed.ToString("N") : null;

    private static AuthClientClass ClassifyClient(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return AuthClientClass.Unknown;

        return ClientRequestClassifier.ResolveIsBrowser(userAgent)
            ? AuthClientClass.Browser
            : AuthClientClass.ApiClient;
    }

    private static AuthDeviceClass ClassifyDevice(string userAgent) =>
        ClientRequestClassifier.ResolveDeviceType(userAgent) switch
        {
            "Desktop" => AuthDeviceClass.Desktop,
            "Mobile" => AuthDeviceClass.Mobile,
            "Tablet" => AuthDeviceClass.Tablet,
            "API Client" => AuthDeviceClass.ApiClient,
            _ => AuthDeviceClass.Unknown,
        };
}
