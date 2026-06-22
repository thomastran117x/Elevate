namespace backend.main.application.openapi
{
    /// <summary>
    /// Human-readable summaries and descriptions for every API operation.
    /// Keyed by "HTTP_METHOD /normalized/path" matching the OpenAPI path template.
    /// </summary>
    internal static class OpenApiDescriptions
    {
        internal static readonly IReadOnlyDictionary<string, OperationMeta> Operations =
            new Dictionary<string, OperationMeta>(StringComparer.OrdinalIgnoreCase)
            {
                // ── Admin ────────────────────────────────────────────────────────────────

                ["GET /api/admin/clubs/posts"] = new("List all club posts"),
                ["POST /api/admin/clubs/posts/reindex"] = new("Rebuild the club posts search index"),
                ["POST /api/admin/clubs/reindex"] = new("Rebuild the clubs search index"),
                ["POST /api/admin/events/reindex"] = new("Rebuild the events search index"),
                ["PATCH /api/admin/users/{id}/status"] = new(
                    "Update a user's account status",
                    "Enables or disables a user account. Disabling immediately revokes all active sessions."
                ),

                // ── Auth ─────────────────────────────────────────────────────────────────

                ["POST /api/auth/login"] = new("Sign in with email and password"),
                ["POST /api/auth/signup"] = new(
                    "Create an account with email and password",
                    "Registers a new account and sends a verification email. The account cannot be used until the email address is confirmed."
                ),
                ["POST /api/auth/verify/otp"] = new("Verify email with a one-time code"),
                ["GET /api/auth/verify"] = new(
                    "Complete email verification via link",
                    "Processes the `token` query parameter from the verification email link. On success, redirects to the frontend with the verification result."
                ),
                ["POST /api/auth/verify"] = new("Resend the verification email"),
                ["POST /api/auth/google"] = new("Sign in or sign up with Google"),
                ["POST /api/auth/google/code"] = new(
                    "Exchange a Google authorization code for a session",
                    "Completes the server-side OAuth 2.0 code flow by exchanging a Google authorization code for tokens and returning a session."
                ),
                ["POST /api/auth/microsoft"] = new("Sign in or sign up with Microsoft"),
                ["POST /api/auth/oauth/complete"] = new(
                    "Complete OAuth sign-up by selecting a role",
                    "Finalizes the OAuth sign-up flow after the user selects a role. Requires the `signupToken` returned by the OAuth initiation endpoint."
                ),
                ["GET /api/auth/me"] = new("Get the current authenticated user"),
                ["GET /api/auth/csrf"] = new(
                    "Get the CSRF token",
                    "Returns the CSRF token required by protected browser-oriented auth POST endpoints."
                ),
                ["POST /api/auth/refresh"] = new(
                    "Refresh the browser session",
                    "Browser-cookie session refresh. Requires the CSRF header and the refresh cookies issued at sign-in."
                ),
                ["POST /api/auth/api/refresh"] = new(
                    "Refresh an API-token session",
                    "API-token session refresh. Supply the refresh token and session-binding token via request headers or the request body."
                ),
                ["POST /api/auth/logout"] = new("Sign out of the browser session"),
                ["POST /api/auth/api/logout"] = new("Sign out of an API-token session"),
                ["GET /api/auth/device/verify"] = new(
                    "Verify a new device via email link",
                    "Processes the one-click verification link emailed when a sign-in from an unrecognized device is detected. On success, redirects to the frontend."
                ),
                ["POST /api/auth/device/verify"] = new("Confirm a new device using a one-time code"),
                ["GET /api/auth/mfa"] = new("Get SMS MFA enrollment status"),
                ["POST /api/auth/mfa/enroll/start"] = new("Start SMS MFA enrollment"),
                ["POST /api/auth/mfa/enroll/verify"] = new("Verify an SMS MFA enrollment code"),
                ["POST /api/auth/mfa/disable"] = new("Disable SMS MFA"),
                ["POST /api/auth/forgot-password"] = new("Request a password reset email"),
                ["POST /api/auth/change-password"] = new("Reset the account password"),

                // ── Clubs ────────────────────────────────────────────────────────────────

                ["POST /api/clubs"] = new("Create a club"),
                ["GET /api/clubs"] = new("List clubs"),
                ["POST /api/clubs/search"] = new("Search clubs"),
                ["GET /api/clubs/managed"] = new("List clubs managed by the current user"),
                ["POST /api/clubs/{clubId}/join"] = new("Join a club"),
                ["DELETE /api/clubs/{clubId}/join"] = new("Leave a club"),
                ["GET /api/clubs/{clubId}/members"] = new("List club members"),
                ["GET /api/clubs/{clubId}/members/me"] = new("Check if the current user is a member"),
                ["GET /api/clubs/{id}"] = new("Get a club"),
                ["PUT /api/clubs/{id}"] = new("Update a club"),
                ["DELETE /api/clubs/{id}"] = new("Delete a club"),
                ["GET /api/clubs/{id}/staff"] = new("List club staff"),
                ["POST /api/clubs/{id}/staff/managers"] = new("Add a manager to a club"),
                ["POST /api/clubs/{id}/staff/volunteers"] = new("Add a volunteer to a club"),
                ["DELETE /api/clubs/{id}/staff/{userId}"] = new("Remove a staff member from a club"),
                ["POST /api/clubs/{id}/transfer-ownership"] = new(
                    "Transfer club ownership",
                    "Transfers ownership to the specified user. The current owner is demoted to manager. This action cannot be reversed by club staff."
                ),
                ["GET /api/clubs/{id}/versions"] = new("List club version history"),
                ["GET /api/clubs/{id}/versions/{versionNumber}"] = new("Get a specific club version"),
                ["POST /api/clubs/{id}/versions/{versionNumber}/rollback"] = new(
                    "Roll back a club to an earlier version",
                    "Restores the club to the state captured at the given version. A new version entry is recorded to track the rollback."
                ),

                // ── Club posts ───────────────────────────────────────────────────────────

                ["POST /api/clubs/{clubId}/posts"] = new("Create a club post"),
                ["GET /api/clubs/{clubId}/posts"] = new("List posts for a club"),
                ["GET /api/clubs/{clubId}/posts/{id}"] = new("Get a club post"),
                ["PUT /api/clubs/{clubId}/posts/{id}"] = new("Update a club post"),
                ["DELETE /api/clubs/{clubId}/posts/{id}"] = new("Delete a club post"),

                // ── Post comments ────────────────────────────────────────────────────────

                ["POST /api/clubs/{clubId}/posts/{postId}/comments"] = new("Add a comment to a post"),
                ["GET /api/clubs/{clubId}/posts/{postId}/comments"] = new("List comments on a post"),
                ["GET /api/clubs/{clubId}/posts/{postId}/comments/events"] = new(
                    "Stream live comment events",
                    "Opens a Server-Sent Events (SSE) stream that pushes new and updated comments on the post in real time."
                ),
                ["PUT /api/clubs/{clubId}/posts/{postId}/comments/{commentId}"] = new("Update a comment"),
                ["DELETE /api/clubs/{clubId}/posts/{postId}/comments/{commentId}"] = new("Delete a comment"),

                // ── Club reviews ─────────────────────────────────────────────────────────

                ["POST /api/clubs/{clubId}/reviews"] = new("Create a club review"),
                ["GET /api/clubs/{clubId}/reviews"] = new("List reviews for a club"),
                ["PUT /api/clubs/{clubId}/reviews/{reviewId}"] = new("Update a club review"),
                ["DELETE /api/clubs/{clubId}/reviews/{reviewId}"] = new("Delete a club review"),

                // ── Events ───────────────────────────────────────────────────────────────

                ["POST /api/events/{clubId}"] = new("Create an event"),
                ["GET /api/events"] = new("List published events"),
                ["POST /api/events/search"] = new("Search events"),
                ["GET /api/events/batch"] = new(
                    "Get multiple events by ID",
                    "Fetches multiple events by their IDs in a single request. Pass `id` as a repeated or comma-separated query parameter."
                ),
                ["PUT /api/events/batch"] = new("Bulk update events"),
                ["DELETE /api/events/batch"] = new("Bulk delete events"),
                ["POST /api/events/batch/{clubId}"] = new(
                    "Bulk create events for a club",
                    "Creates multiple events in a single request. Returns a partial-success result — check the `failed` array for items that could not be created."
                ),
                ["POST /api/events/batch/register"] = new(
                    "Register for multiple events",
                    "Registers the current user for multiple events at once. Returns a partial-success result — check the `failed` array for events where registration did not succeed."
                ),
                ["DELETE /api/events/batch/register"] = new(
                    "Cancel multiple event registrations",
                    "Cancels registrations for multiple events in a single request."
                ),
                ["GET /api/events/clubs/{clubId}"] = new("List published events for a club"),
                ["GET /api/events/clubs/{clubId}/manage"] = new(
                    "List all events for a club (management view)",
                    "Returns all events for a club including drafts, cancelled, and archived events. Requires club manager or owner access."
                ),
                ["GET /api/events/clubs/{clubId}/analytics"] = new("Get analytics for all events in a club"),
                ["POST /api/events/clubs/{clubId}/drafts"] = new("Create a draft event"),
                ["POST /api/events/images/presigned-url"] = new(
                    "Get a presigned URL for image upload",
                    "Returns a short-lived presigned URL for direct S3 upload. After uploading, call `POST /api/events/{eventId}/images` with the resulting object key to attach the image to an event."
                ),
                ["GET /api/events/{eventId}"] = new("Get an event"),
                ["PUT /api/events/{eventId}"] = new("Update an event"),
                ["DELETE /api/events/{eventId}"] = new("Delete an event"),
                ["PATCH /api/events/{eventId}/draft"] = new("Update a draft event"),
                ["POST /api/events/{eventId}/publish"] = new("Publish an event"),
                ["POST /api/events/{eventId}/cancel"] = new("Cancel an event"),
                ["POST /api/events/{eventId}/archive"] = new("Archive an event"),
                ["GET /api/events/{eventId}/manage"] = new("Get an event (management view)"),
                ["GET /api/events/{eventId}/analytics"] = new("Get event analytics"),
                ["POST /api/events/{eventId}/images"] = new("Attach an uploaded image to an event"),
                ["DELETE /api/events/{eventId}/images/{imageId}"] = new("Remove an image from an event"),
                ["GET /api/events/{eventId}/versions"] = new("List event version history"),
                ["GET /api/events/{eventId}/versions/{versionNumber}"] = new("Get a specific event version"),
                ["POST /api/events/{eventId}/versions/{versionNumber}/rollback"] = new(
                    "Roll back an event to an earlier version",
                    "Restores the event to the state captured at the given version. A new version entry is recorded to track the rollback."
                ),

                // ── Event invitations ────────────────────────────────────────────────────

                ["POST /api/events/{eventId}/invitations"] = new("Create invitations for an event"),
                ["GET /api/events/{eventId}/invitations"] = new("List invitations for an event"),
                ["POST /api/events/{eventId}/invitations/{invitationId}/revoke"] = new("Revoke an invitation"),
                ["POST /api/events/{eventId}/invitation-links"] = new("Create a shareable invitation link"),
                ["GET /api/events/{eventId}/invitation-links"] = new("List invitation links for an event"),
                ["POST /api/events/{eventId}/invitation-links/{linkId}/revoke"] = new("Revoke an invitation link"),
                ["POST /api/events/invitations/resolve"] = new(
                    "Resolve an invitation token",
                    "Validates an invitation token from an email link or shareable link and returns the associated event details. Use this to preview the invitation before prompting the user to accept or decline."
                ),
                ["POST /api/events/invitations/accept"] = new("Accept an invitation by token"),
                ["POST /api/events/invitations/{invitationId}/accept"] = new("Accept an invitation by ID"),
                ["POST /api/events/invitations/decline"] = new("Decline an invitation by token"),
                ["POST /api/events/invitations/{invitationId}/decline"] = new("Decline an invitation by ID"),
                ["GET /api/events/me/invited"] = new("List invitations for the current user"),

                // ── Event registrations ──────────────────────────────────────────────────

                ["POST /api/events/{eventId}/register"] = new("Register for an event"),
                ["DELETE /api/events/{eventId}/register"] = new("Cancel an event registration"),
                ["PATCH /api/events/{eventId}/register"] = new("Update an event registration"),
                ["GET /api/events/{eventId}/registrations"] = new("List registrations for an event"),
                ["GET /api/events/{eventId}/registrations/me"] = new("Check current user registration status"),

                // ── Payments ─────────────────────────────────────────────────────────────

                ["POST /api/payments/{eventId}"] = new("Create a Stripe checkout session"),
                ["GET /api/payments/{paymentId}"] = new("Get a payment"),
                ["GET /api/payments/me"] = new("List the current user's payments"),
                ["POST /api/payments/webhook"] = new(
                    "Handle a Stripe webhook event",
                    "Validates and processes a Stripe webhook event using the raw request payload and `Stripe-Signature` header. The endpoint must receive the unmodified raw body."
                ),
                ["POST /api/payments/{paymentId}/refund"] = new("Refund a payment"),

                // ── Users ────────────────────────────────────────────────────────────────

                ["GET /api/users/{userId}/clubs/following"] = new("List clubs followed by a user"),
                ["GET /api/users/{userId}/events/registered"] = new("List events a user is registered for"),
                ["GET /api/users/{userId}/reviews"] = new("List reviews written by a user"),
            };
    }

    internal readonly record struct OperationMeta(string Summary, string? Description = null);
}

