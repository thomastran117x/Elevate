namespace backend.main.features.auth.events;

/// <summary>
/// Per-request state shared between the code that rejects a request and the authorization denial
/// observer, which only sees the final status code.
/// </summary>
public sealed class AuthSecurityRequestFeature
{
    /// <summary>Why the request was refused, when the code refusing it knows.</summary>
    public AuthSecurityReason? DenialReason
    {
        get; set;
    }

    /// <summary>
    /// Whether an event already recorded on this request explains a 403 response, so that 403 is
    /// not counted again as an authorization denial. Unrelated events, such as a session revoked
    /// earlier in the request, do not count.
    /// </summary>
    public bool DenialExplained
    {
        get; set;
    }

    /// <summary>
    /// Whether an event describes a refusal answered with 403: a disabled account turned away, or a
    /// sign-in held for new-device verification. Every other 403 is an authorization denial.
    /// </summary>
    public static bool ExplainsForbidden(AuthSecurityEvent securityEvent) =>
        securityEvent.Definition != AuthSecurityEvents.AuthorizationDenied
        && securityEvent.Reason switch
        {
            AuthSecurityReason.AccountDisabled => securityEvent.Outcome == AuthSecurityOutcome.Denied,
            AuthSecurityReason.DeviceVerificationRequired => true,
            _ => false,
        };

    public static AuthSecurityRequestFeature For(HttpContext context)
    {
        var feature = context.Features.Get<AuthSecurityRequestFeature>();
        if (feature is null)
        {
            feature = new AuthSecurityRequestFeature();
            context.Features.Set(feature);
        }

        return feature;
    }

    /// <summary>Records why the current request is about to be refused with 403.</summary>
    public static void MarkDenial(HttpContext context, AuthSecurityReason reason) =>
        For(context).DenialReason = reason;
}
