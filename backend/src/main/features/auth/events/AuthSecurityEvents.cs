namespace backend.main.features.auth.events;

/// <summary>
/// A stable identifier for one kind of authentication security event.
/// </summary>
/// <param name="Id">The numeric <see cref="EventId"/>; unique across the catalog.</param>
/// <param name="Name">The dotted wire name dashboards, alerts, and security history key on.</param>
/// <param name="Level">The log level the logging sink writes the event at.</param>
public sealed record AuthSecurityEventDefinition(int Id, string Name, LogLevel Level)
{
    public EventId EventId => new(Id, Name);
}

/// <summary>
/// The catalog of authentication security events, documented in <c>docs/SECURITY_EVENTS.md</c>.
/// </summary>
/// <remarks>
/// Append only. Ids and names are a contract with dashboards, alerts, and the persisted security
/// history, so an entry is never renumbered, renamed, or reused. The 32xxx range belongs to the
/// abuse-protection store's operational events.
/// </remarks>
public static class AuthSecurityEvents
{
    public static readonly AuthSecurityEventDefinition SignInSucceeded =
        new(33001, "auth.sign_in.succeeded", LogLevel.Information);

    public static readonly AuthSecurityEventDefinition SignInFailed =
        new(33002, "auth.sign_in.failed", LogLevel.Warning);

    public static readonly AuthSecurityEventDefinition SignInChallenged =
        new(33003, "auth.sign_in.challenged", LogLevel.Information);

    public static readonly AuthSecurityEventDefinition Throttled =
        new(33010, "auth.throttled", LogLevel.Warning);

    public static readonly AuthSecurityEventDefinition RecoveryRequested =
        new(33020, "auth.recovery.requested", LogLevel.Information);

    public static readonly AuthSecurityEventDefinition RecoveryCompleted =
        new(33021, "auth.recovery.completed", LogLevel.Information);

    public static readonly AuthSecurityEventDefinition VerificationFailed =
        new(33030, "auth.verification.failed", LogLevel.Warning);

    public static readonly AuthSecurityEventDefinition StepUpSucceeded =
        new(33040, "auth.step_up.succeeded", LogLevel.Information);

    public static readonly AuthSecurityEventDefinition StepUpFailed =
        new(33041, "auth.step_up.failed", LogLevel.Warning);

    public static readonly AuthSecurityEventDefinition FactorChanged =
        new(33050, "auth.factor.changed", LogLevel.Information);

    public static readonly AuthSecurityEventDefinition FactorChangeFailed =
        new(33051, "auth.factor.change_failed", LogLevel.Warning);

    public static readonly AuthSecurityEventDefinition RefreshTokenReused =
        new(33060, "auth.refresh_token.reused", LogLevel.Warning);

    public static readonly AuthSecurityEventDefinition SessionRevoked =
        new(33070, "auth.session.revoked", LogLevel.Information);

    public static readonly AuthSecurityEventDefinition ProviderLinked =
        new(33080, "auth.provider.linked", LogLevel.Information);

    public static readonly AuthSecurityEventDefinition ProviderLinkConflict =
        new(33081, "auth.provider_link.conflict", LogLevel.Warning);

    public static readonly AuthSecurityEventDefinition AuthorizationDenied =
        new(33090, "authz.denied", LogLevel.Warning);

    public static readonly AuthSecurityEventDefinition AccountStatusChanged =
        new(33100, "auth.account.status_changed", LogLevel.Information);

    public static readonly AuthSecurityEventDefinition RecoveryCodeUsed =
        new(33110, "auth.recovery_code.used", LogLevel.Information);

    public static readonly IReadOnlyList<AuthSecurityEventDefinition> All =
    [
        SignInSucceeded,
        SignInFailed,
        SignInChallenged,
        Throttled,
        RecoveryRequested,
        RecoveryCompleted,
        VerificationFailed,
        StepUpSucceeded,
        StepUpFailed,
        FactorChanged,
        FactorChangeFailed,
        RefreshTokenReused,
        SessionRevoked,
        ProviderLinked,
        ProviderLinkConflict,
        AuthorizationDenied,
        AccountStatusChanged,
        RecoveryCodeUsed,
    ];
}
