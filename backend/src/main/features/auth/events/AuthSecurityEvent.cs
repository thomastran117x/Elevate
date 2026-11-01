namespace backend.main.features.auth.events;

/// <summary>
/// One authentication security event.
/// </summary>
/// <remarks>
/// Every field is typed so that nothing a caller or client supplies can reach a log line, metric,
/// or the persisted history as free text: classifications are enums from closed sets, identifiers
/// are numeric account ids or server-generated session ids, and contact or network identifiers
/// only appear as keyed hashes. Callers set the classification fields; the emitter owns and
/// overwrites everything under "Enrichment".
/// </remarks>
public sealed record AuthSecurityEvent
{
    public required AuthSecurityEventDefinition Definition
    {
        get; init;
    }

    public required AuthSecurityOutcome Outcome
    {
        get; init;
    }

    public AuthSecurityReason Reason { get; init; } = AuthSecurityReason.None;

    public AuthSecurityMethod? Method
    {
        get; init;
    }

    public AuthSecurityFlow? Flow
    {
        get; init;
    }

    public AuthThrottleDimension? Dimension
    {
        get; init;
    }

    public AuthSecurityFactor? Factor
    {
        get; init;
    }

    public AuthFactorChange? Change
    {
        get; init;
    }

    /// <summary>The account the event is about.</summary>
    public int? UserId
    {
        get; init;
    }

    /// <summary>The account that caused the event, when it is not <see cref="UserId"/> (an admin).</summary>
    public int? ActorUserId
    {
        get; init;
    }

    /// <summary>The refresh session, or null when the event covers every session of the account.</summary>
    public string? SessionId
    {
        get; init;
    }

    // Enrichment

    public string? TargetHash
    {
        get; init;
    }

    public string? SourceIpHash
    {
        get; init;
    }

    public AuthClientClass ClientClass { get; init; } = AuthClientClass.Unknown;

    public AuthDeviceClass DeviceClass { get; init; } = AuthDeviceClass.Unknown;

    public string? CorrelationId
    {
        get; init;
    }

    /// <summary>The HTTP method and route template, for <c>authz.denied</c> only.</summary>
    public string? Route
    {
        get; init;
    }

    public DateTimeOffset OccurredAt
    {
        get; init;
    }
}

/// <summary>
/// Separates expected rejections from service faults, so a dashboard can tell an attack or a
/// mistyped password from an outage without the client ever seeing the difference.
/// </summary>
public enum AuthSecurityOutcome
{
    Succeeded,
    Denied,
    Throttled,
    Faulted,
}

/// <summary>
/// A closed set of reason codes. New values are appended; names are the wire contract.
/// </summary>
public enum AuthSecurityReason
{
    None,
    InvalidCredentials,
    CaptchaFailed,
    AccountDisabled,
    ProviderRejected,
    EmailUnverified,
    DeviceVerificationRequired,
    StepUpRequired,
    LimitExceeded,
    Issued,
    Suppressed,
    ProviderReminder,
    InvalidCode,
    Expired,
    AttemptsExhausted,
    Replayed,
    AuthVersionMismatch,
    InvalidState,
    InvalidRequest,
    Rejected,
    BindingMissing,
    BindingMismatch,
    TransportMismatch,
    UserMismatch,
    Logout,
    TokenReuse,
    PasswordChanged,
    PasswordReset,
    EmailChanged,
    AccountDeleted,
    AccountEnabled,
    SessionEnded,
    EmailBoundToOtherProvider,
    Policy,
    MfaRequired,
    ResourceAccess,
}

public enum AuthSecurityMethod
{
    Password,
    Google,
    Microsoft,
    Sms,
    Totp,
    Email,
    RefreshToken,
    RecoveryCode,
}

/// <summary>The auth flow, verification purpose, or throttled budget an event belongs to.</summary>
public enum AuthSecurityFlow
{
    General,
    Login,
    Signup,
    Recovery,
    PasswordRecovery,
    UsernameRecovery,
    PasswordReset,
    EmailChange,
    DeviceVerification,
    LoginStepUp,
    SessionStepUp,
    OAuthCompletion,
    MfaDelivery,
    MfaEnrollment,
    Verification,
    EmailAvailability,
    UsernameAvailability,
    UsernameSuggestions,
    Refresh,
}

public enum AuthThrottleDimension
{
    SourceIp,
    Target,
    Account,
    Attempts,
    RateLimiter,
    Concurrency,
}

public enum AuthSecurityFactor
{
    Sms,
    Totp,
    Password,
    Email,
    RecoveryCodes,
}

public enum AuthFactorChange
{
    Enabled,
    Disabled,
    Removed,
    Changed,
    Regenerated,
}

public enum AuthClientClass
{
    Unknown,
    Browser,
    ApiClient,
}

public enum AuthDeviceClass
{
    Unknown,
    Desktop,
    Mobile,
    Tablet,
    ApiClient,
}
