using backend.main.features.auth.events;

namespace backend.main.features.auth.history;

/// <summary>
/// Decides which auth security events belong in the account owner's security history, and as what.
/// </summary>
/// <remarks>
/// Every catalog definition is listed in exactly one of <see cref="Recorded"/> and
/// <see cref="NotRecorded"/>, and a unit test enforces it, so an event added to the catalog later
/// cannot reach the history, or be left out of it, without someone deciding.
/// <para>
/// Left out on purpose: failed sign-ins and verification, which would let anyone who knows a
/// username write noise into the history; throttling; recovery requests, whose reasons
/// (issued, suppressed, provider_reminder) must never reach the requester; step-up results;
/// failed factor changes; provider link conflicts; and authorization denials.
/// </para>
/// </remarks>
public static class SecurityHistoryCatalog
{
    public static readonly IReadOnlySet<AuthSecurityEventDefinition> Recorded =
        new HashSet<AuthSecurityEventDefinition>
        {
            AuthSecurityEvents.SignInSucceeded,
            AuthSecurityEvents.SignInChallenged,
            AuthSecurityEvents.RecoveryCompleted,
            AuthSecurityEvents.FactorChanged,
            AuthSecurityEvents.RefreshTokenReused,
            AuthSecurityEvents.SessionRevoked,
            AuthSecurityEvents.ProviderLinked,
            AuthSecurityEvents.AccountStatusChanged,
            AuthSecurityEvents.RecoveryCodeUsed,
        };

    public static readonly IReadOnlySet<AuthSecurityEventDefinition> NotRecorded =
        new HashSet<AuthSecurityEventDefinition>
        {
            AuthSecurityEvents.SignInFailed,
            AuthSecurityEvents.Throttled,
            AuthSecurityEvents.RecoveryRequested,
            AuthSecurityEvents.VerificationFailed,
            AuthSecurityEvents.StepUpSucceeded,
            AuthSecurityEvents.StepUpFailed,
            AuthSecurityEvents.FactorChangeFailed,
            AuthSecurityEvents.ProviderLinkConflict,
            AuthSecurityEvents.AuthorizationDenied,
        };

    /// <summary>
    /// The history entry an event becomes, or null when the owner should not see it: the event is
    /// not recorded, concerns no known account, faulted, or carries a classification with nothing
    /// to tell the owner.
    /// </summary>
    public static AccountSecurityEventKind? Classify(AuthSecurityEvent securityEvent)
    {
        if (securityEvent.UserId is null || securityEvent.Outcome == AuthSecurityOutcome.Faulted)
            return null;

        var definition = securityEvent.Definition;
        if (definition == AuthSecurityEvents.SignInSucceeded)
            return AccountSecurityEventKind.SignedIn;

        if (definition == AuthSecurityEvents.SignInChallenged)
        {
            return securityEvent.Reason switch
            {
                AuthSecurityReason.DeviceVerificationRequired => AccountSecurityEventKind.NewDeviceChallenge,
                AuthSecurityReason.StepUpRequired => AccountSecurityEventKind.SignInChallenge,
                _ => null,
            };
        }

        if (definition == AuthSecurityEvents.RecoveryCompleted)
            return AccountSecurityEventKind.PasswordReset;

        if (definition == AuthSecurityEvents.FactorChanged)
            return ClassifyFactorChange(securityEvent.Factor, securityEvent.Change);

        if (definition == AuthSecurityEvents.ProviderLinked)
            return AccountSecurityEventKind.ProviderLinked;

        if (definition == AuthSecurityEvents.RefreshTokenReused)
            return AccountSecurityEventKind.RefreshTokenReuseDetected;

        if (definition == AuthSecurityEvents.SessionRevoked && securityEvent.Outcome == AuthSecurityOutcome.Succeeded)
            return ClassifyRevocation(securityEvent.Reason);

        if (definition == AuthSecurityEvents.RecoveryCodeUsed)
            return AccountSecurityEventKind.RecoveryCodeUsed;

        if (definition == AuthSecurityEvents.AccountStatusChanged)
        {
            return securityEvent.Reason switch
            {
                AuthSecurityReason.AccountDisabled => AccountSecurityEventKind.AccountDisabled,
                AuthSecurityReason.AccountEnabled => AccountSecurityEventKind.AccountEnabled,
                _ => null,
            };
        }

        return null;
    }

    /// <summary>
    /// An administrator, rather than the owner, caused the event. Only the fact is kept; the
    /// administrator's account id is never stored or shown.
    /// </summary>
    public static bool IsInitiatedByAdmin(AuthSecurityEvent securityEvent) =>
        securityEvent.ActorUserId is { } actor && actor != securityEvent.UserId;

    /// <summary>
    /// The reason worth keeping with the entry. Only revocations carry one: "signed out
    /// everywhere" means something different after a password change than after an email change.
    /// </summary>
    public static string? ReasonFor(AuthSecurityEvent securityEvent) =>
        securityEvent.Definition == AuthSecurityEvents.SessionRevoked
            && securityEvent.Reason != AuthSecurityReason.None
            ? AuthSecurityWireNames.Of(securityEvent.Reason)
            : null;

    private static AccountSecurityEventKind? ClassifyFactorChange(
        AuthSecurityFactor? factor,
        AuthFactorChange? change) =>
        (factor, change) switch
        {
            (AuthSecurityFactor.Password, _) => AccountSecurityEventKind.PasswordChanged,
            (AuthSecurityFactor.Email, _) => AccountSecurityEventKind.EmailChanged,
            (AuthSecurityFactor.Sms or AuthSecurityFactor.Totp, AuthFactorChange.Enabled) => AccountSecurityEventKind.MfaEnabled,
            (AuthSecurityFactor.Sms or AuthSecurityFactor.Totp, AuthFactorChange.Disabled) => AccountSecurityEventKind.MfaDisabled,
            (AuthSecurityFactor.Sms or AuthSecurityFactor.Totp, AuthFactorChange.Removed) => AccountSecurityEventKind.MfaRemoved,
            (AuthSecurityFactor.RecoveryCodes, AuthFactorChange.Enabled) => AccountSecurityEventKind.RecoveryCodesGenerated,
            (AuthSecurityFactor.RecoveryCodes, AuthFactorChange.Regenerated) => AccountSecurityEventKind.RecoveryCodesRegenerated,
            _ => null,
        };

    private static AccountSecurityEventKind? ClassifyRevocation(AuthSecurityReason reason) =>
        reason switch
        {
            AuthSecurityReason.Logout => AccountSecurityEventKind.SignedOut,
            AuthSecurityReason.PasswordChanged
                or AuthSecurityReason.PasswordReset
                or AuthSecurityReason.EmailChanged => AccountSecurityEventKind.SessionsRevoked,
            AuthSecurityReason.TransportMismatch
                or AuthSecurityReason.BindingMissing
                or AuthSecurityReason.BindingMismatch
                or AuthSecurityReason.UserMismatch
                or AuthSecurityReason.AuthVersionMismatch => AccountSecurityEventKind.SessionEndedForSecurity,
            // TokenReuse is already recorded by the paired auth.refresh_token.reused event, an
            // admin enable or disable by auth.account.status_changed (which is emitted even when no
            // session was revoked), and AccountDeleted has no owner left to show it to.
            _ => null,
        };
}
