namespace backend.tests.Unit.Application.Security;

/// <summary>
/// The reviewed public surface: every controller action and hub method that anonymous callers
/// may reach. Adding or removing an entry is a deliberate security decision, made in the same
/// change as the endpoint and reviewed by the owners in <c>.github/CODEOWNERS</c>. The
/// authorization gate and the committed OpenAPI contract are both checked against this list.
/// </summary>
public static class ReviewedPublicSurface
{
    /// <summary>Prefix of the hub method keys, which have no HTTP operation of their own.</summary>
    public const string HubPrefix = "HUB ";

    // Keys are "VERB /api/route" for controller actions and "HUB Type.Method" for hub methods.
    public static readonly IReadOnlyList<string> Endpoints =
    [
        // Sign-in, sign-up, verification, OAuth, session refresh/logout, and account recovery.
        "GET /api/auth/csrf",
        "GET /api/auth/device/verify",
        "GET /api/auth/email/availability",
        "GET /api/auth/username/availability",
        "GET /api/auth/username/suggestions",
        "GET /api/auth/verify",
        "GET /api/auth/verify/email-change",
        "POST /api/auth/api/logout",
        "POST /api/auth/api/refresh",
        "POST /api/auth/change-password",
        "POST /api/auth/device/verify",
        "POST /api/auth/email/availability",
        "POST /api/auth/forgot-password",
        "POST /api/auth/google",
        "POST /api/auth/google/code",
        "POST /api/auth/login",
        "POST /api/auth/logout",
        "POST /api/auth/mfa/start",
        "POST /api/auth/mfa/verify",
        "POST /api/auth/mfa/verify/recovery-code",
        "POST /api/auth/mfa/verify/totp",
        "POST /api/auth/microsoft",
        "POST /api/auth/oauth/complete",
        "POST /api/auth/passkeys/sign-in",
        "POST /api/auth/passkeys/sign-in/options",
        "POST /api/auth/recovery/password",
        "POST /api/auth/recovery/username",
        "POST /api/auth/refresh",
        "POST /api/auth/reset-password",
        "POST /api/auth/signup",
        "POST /api/auth/verify",
        "POST /api/auth/verify/email-change",
        "POST /api/auth/verify/otp",

        // Club reads; private clubs are gated in the services.
        "GET /api/clubs",
        "GET /api/clubs/{clubId}/discussions",
        "GET /api/clubs/{clubId}/discussions/{discussionId}/replies",
        "GET /api/clubs/{clubId}/members",
        "GET /api/clubs/{clubId}/posts",
        "GET /api/clubs/{clubId}/posts/{id}",
        "GET /api/clubs/{clubId}/posts/{postId}/comments",
        "GET /api/clubs/{clubId}/reviews",
        "GET /api/clubs/{id}",
        "POST /api/clubs/search",

        // Event reads; private events are filtered by visibility in the services.
        "GET /api/events",
        "GET /api/events/batch",
        "GET /api/events/clubs/{clubId}",
        "GET /api/events/{eventId}",
        "GET /api/events/{eventId}/registrations",
        "POST /api/events/search",

        // Invitation resolution by token, so a recipient can preview before signing in.
        "POST /api/clubs/invitations/resolve",
        "POST /api/clubs/members/invitations/resolve",
        "POST /api/events/invitations/resolve",

        // Public profiles.
        "GET /api/profile/{username}",

        // Stripe webhook; authenticated by the Stripe-Signature header in the service.
        "POST /api/payments/webhook",

        // Club realtime hub; every join re-runs the REST read gate and typing requires a user.
        "HUB ClubRealtimeHub.JoinClub",
        "HUB ClubRealtimeHub.JoinDiscussion",
        "HUB ClubRealtimeHub.JoinPost",
        "HUB ClubRealtimeHub.LeaveClub",
        "HUB ClubRealtimeHub.LeaveDiscussion",
        "HUB ClubRealtimeHub.LeavePost",
        "HUB ClubRealtimeHub.RequestPresence",
        "HUB ClubRealtimeHub.Typing",
    ];
}
