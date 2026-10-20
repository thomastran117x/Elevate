using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace backend.main.features.auth.events;

/// <summary>
/// Low-cardinality authentication metrics on the <c>EventXperience.Auth</c> meter.
/// </summary>
/// <remarks>
/// Every tag value comes from a closed enum or the event catalog. Account ids, session ids,
/// hashes, addresses, routes, and correlation ids belong in the event log, never in a tag: each
/// distinct tag value creates a time series.
/// </remarks>
public sealed class AuthSecurityMetrics
{
    public const string MeterName = "EventXperience.Auth";

    private readonly Counter<long> _events;
    private readonly Counter<long> _sinkFailures;
    private readonly Counter<long> _securityHistoryPublishFailures;
    private readonly Histogram<double> _operationDuration;

    public AuthSecurityMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _events = meter.CreateCounter<long>(
            "auth.security_events",
            unit: "{event}",
            description: "Authentication security events by event, outcome, and classification.");
        _sinkFailures = meter.CreateCounter<long>(
            "auth.security_events.sink_failures",
            unit: "{failure}",
            description: "Security events a sink failed to write.");
        _securityHistoryPublishFailures = meter.CreateCounter<long>(
            "auth.security_history.publish_failures",
            unit: "{failure}",
            description: "Account security history entries lost because the publish to Kafka failed.");
        _operationDuration = meter.CreateHistogram<double>(
            "auth.operation.duration",
            unit: "ms",
            description: "Duration of authentication endpoints by operation and outcome.");
    }

    public void RecordEvent(AuthSecurityEvent securityEvent)
    {
        _events.Add(
            1,
            new TagList
            {
                { "event", securityEvent.Definition.Name },
                { "outcome", AuthSecurityWireNames.Of(securityEvent.Outcome) },
                { "reason", AuthSecurityWireNames.Of(securityEvent.Reason) },
                { "method", AuthSecurityWireNames.Of(securityEvent.Method) ?? "none" },
                { "flow", AuthSecurityWireNames.Of(securityEvent.Flow) ?? "none" },
                { "dimension", AuthSecurityWireNames.Of(securityEvent.Dimension) ?? "none" },
                { "factor", AuthSecurityWireNames.Of(securityEvent.Factor) ?? "none" },
            });
    }

    public void RecordSinkFailure(string sink) =>
        _sinkFailures.Add(1, new KeyValuePair<string, object?>("sink", sink));

    public void RecordSecurityHistoryPublishFailure() =>
        _securityHistoryPublishFailures.Add(1);

    public void RecordOperation(AuthOperation operation, AuthSecurityOutcome outcome, TimeSpan elapsed) =>
        _operationDuration.Record(
            elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("operation", AuthSecurityWireNames.Of(operation)),
            new KeyValuePair<string, object?>("outcome", AuthSecurityWireNames.Of(outcome)));
}

/// <summary>An authentication endpoint family timed by <c>auth.operation.duration</c>.</summary>
public enum AuthOperation
{
    SignIn,
    OAuthSignIn,
    Refresh,
    StepUpVerify,
    RecoveryRequest,
    PasswordReset,
    Verification,
    FactorChange,
}
