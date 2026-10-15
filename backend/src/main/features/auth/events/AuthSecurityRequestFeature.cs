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
    /// Whether a more specific security event already describes this request, such as a disabled
    /// account's failed sign-in, so its 403 is not counted again as an authorization denial.
    /// </summary>
    public bool EventRecorded
    {
        get; set;
    }

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
