namespace backend.main.features.auth.history;

/// <summary>
/// What an account security history entry records, as the owner sees it.
/// </summary>
/// <remarks>
/// Stored and returned by name (snake_case on the wire), never by ordinal, but still append only:
/// names are a contract with stored rows, the worker, the alert configuration, and the frontend.
/// </remarks>
public enum AccountSecurityEventKind
{
    SignedIn,
    NewDeviceChallenge,
    SignInChallenge,
    PasswordReset,
    PasswordChanged,
    EmailChanged,
    MfaEnabled,
    MfaDisabled,
    MfaRemoved,
    ProviderLinked,
    SignedOut,
    SessionsRevoked,
    SessionEndedForSecurity,
    RefreshTokenReuseDetected,
    AccountDisabled,
    AccountEnabled,
    RecoveryCodesGenerated,
    RecoveryCodesRegenerated,
    RecoveryCodeUsed,
}
