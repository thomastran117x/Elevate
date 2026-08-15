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

                ["POST /api/auth/login"] = new("Sign in with username and password", "Returns a session immediately for trusted devices, or a sign-in verification challenge when the device needs step-up verification."),
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
                ["POST /api/auth/google"] = new("Sign in or sign up with Google", "Returns a session for trusted existing users, a step-up challenge for untrusted existing users, or role-selection details for first-time sign-up."),
                ["POST /api/auth/google/code"] = new(
                    "Exchange a Google authorization code for sign-in",
                    "Completes the server-side OAuth 2.0 code flow and returns either an authenticated session, a sign-in verification challenge, or role-selection details."
                ),
                ["POST /api/auth/microsoft"] = new("Sign in or sign up with Microsoft", "Returns a session for trusted existing users, a step-up challenge for untrusted existing users, or role-selection details for first-time sign-up."),
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
                    "Complete email-based sign-in verification",
                    "Processes the emailed verification link for a new-device sign-in challenge and redirects to the frontend on success."
                ),
                ["POST /api/auth/device/verify"] = new("Complete email-based sign-in verification"),
                ["POST /api/auth/mfa/start"] = new("Start sign-in verification delivery", "Chooses SMS, email, or authenticator verification for an active sign-in challenge, rotates the challenge token when needed, and starts the selected delivery flow."),
                ["POST /api/auth/mfa/verify"] = new("Verify an SMS sign-in code", "Validates an SMS code for an active sign-in verification challenge and completes login on success."),
                ["POST /api/auth/mfa/verify/totp"] = new("Verify an authenticator sign-in code", "Validates a TOTP code for an active sign-in verification challenge and completes login on success."),
                ["GET /api/auth/mfa"] = new("Get MFA security settings"),
                ["POST /api/auth/mfa/enroll/start"] = new("Start SMS MFA enrollment"),
                ["POST /api/auth/mfa/enroll/verify"] = new("Verify an SMS MFA enrollment or re-enable code"),
                ["POST /api/auth/mfa/enable/start"] = new("Start SMS MFA re-enable"),
                ["POST /api/auth/mfa/disable"] = new("Disable SMS MFA"),
                ["POST /api/auth/mfa/remove"] = new("Remove SMS MFA"),
                ["POST /api/auth/mfa/sms/enroll/start"] = new("Start SMS MFA enrollment"),
                ["POST /api/auth/mfa/sms/enroll/verify"] = new("Verify an SMS MFA enrollment or re-enable code"),
                ["POST /api/auth/mfa/sms/enable/start"] = new("Start SMS MFA re-enable"),
                ["POST /api/auth/mfa/sms/disable"] = new("Disable SMS MFA"),
                ["POST /api/auth/mfa/sms/remove"] = new("Remove SMS MFA"),
                ["POST /api/auth/mfa/totp/enroll/start"] = new("Start authenticator app enrollment"),
                ["POST /api/auth/mfa/totp/enroll/verify"] = new("Verify an authenticator enrollment code"),
                ["POST /api/auth/mfa/totp/enable"] = new("Re-enable authenticator app MFA"),
                ["POST /api/auth/mfa/totp/disable"] = new("Disable authenticator app MFA"),
                ["POST /api/auth/mfa/totp/remove"] = new("Remove authenticator app MFA"),
                ["POST /api/auth/forgot-password"] = new("Start password recovery by username (compatibility alias)"),
                ["POST /api/auth/change-password"] = new("Reset the account password (compatibility alias)"),
                ["POST /api/auth/recovery/password"] = new("Start password recovery by username"),
                ["POST /api/auth/recovery/username"] = new("Recover a username by email"),
                ["POST /api/auth/reset-password"] = new("Reset the account password"),

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

                // ── Club discussions ─────────────────────────────────────────────────────

                ["POST /api/clubs/{clubId}/discussions"] = new("Start a club discussion"),
                ["GET /api/clubs/{clubId}/discussions"] = new("List discussions for a club"),
                ["PUT /api/clubs/{clubId}/discussions/{discussionId}"] = new("Update a club discussion"),
                ["DELETE /api/clubs/{clubId}/discussions/{discussionId}"] = new("Delete a club discussion"),
                ["GET /api/clubs/{clubId}/discussions/{discussionId}/replies"] = new("List one level of discussion replies"),
                ["POST /api/clubs/{clubId}/discussions/{discussionId}/replies"] = new("Add a discussion reply"),
                ["PUT /api/clubs/{clubId}/discussions/{discussionId}/replies/{replyId}"] = new("Edit a discussion reply"),
                ["DELETE /api/clubs/{clubId}/discussions/{discussionId}/replies/{replyId}"] = new("Soft-delete a discussion reply"),
                ["PUT /api/clubs/{clubId}/discussions/{discussionId}/replies/{replyId}/reaction"] = new("Set a reply reaction"),
                ["DELETE /api/clubs/{clubId}/discussions/{discussionId}/replies/{replyId}/reaction"] = new("Clear a reply reaction"),
                ["GET /api/clubs/{clubId}/discussions/replies/events"] = new(
                    "Stream live discussion reply events",
                    "Opens a club-wide Server-Sent Events stream for reply creation, editing, deletion, and reaction counts."
                ),

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
                // ── Event recurrence series ──────────────────────────────────────────────

                ["POST /api/events/clubs/{clubId}/series/preview"] = new(
                    "Preview a recurrence rule",
                    "Expands a repeat rule without saving anything. Occurrences are generated by advancing local wall-clock time in the rule's IANA time zone and converting each one to UTC individually, so a weekly 7pm event stays at 7pm across a daylight-saving change. Check `warnings` for occurrences that fell in a DST gap, landed on an ambiguous hour, or were clamped to a shorter month."
                ),
                ["POST /api/events/{eventId}/series"] = new(
                    "Create a recurrence series from a draft",
                    "Turns an existing draft into the first occurrence of a series and materializes the rest as drafts. Occurrences are ordinary events, so they appear in search and listings and support registration exactly like any other event."
                ),
                ["GET /api/events/series/{seriesId}"] = new("Get a recurrence series and its occurrences"),
                ["GET /api/events/clubs/{clubId}/series"] = new("List a club's recurrence series"),
                ["POST /api/events/series/{seriesId}/extend"] = new(
                    "Extend a recurrence series",
                    "Generates the additional occurrences a higher count or later end date implies. Occurrences that already exist are left untouched."
                ),
                ["POST /api/events/series/{seriesId}/publish"] = new(
                    "Publish a recurrence series",
                    "Publishes every draft occurrence that passes its publish checks. Occurrences that do not — for example one whose start time has already passed — are listed in `skipped` rather than failing the request."
                ),
                ["PATCH /api/events/series/{seriesId}/occurrences"] = new(
                    "Update this and all future occurrences",
                    "Applies a patch to the given occurrence and every later one that has not yet started. Occurrences edited individually are skipped unless `includeOverridden` is set, and any occurrence whose registrations would be invalidated is reported in `skipped` instead of blocking the rest."
                ),
                ["POST /api/events/series/{seriesId}/cancel"] = new(
                    "Cancel a recurrence series",
                    "Cancels the series' occurrences without deleting anything. To cancel a single occurrence, use `POST /api/events/{eventId}/cancel` instead — that leaves the series and its other occurrences intact."
                ),
                ["DELETE /api/events/series/{seriesId}"] = new(
                    "Delete a recurrence series",
                    "Deletes the series row. Occurrences that anyone has registered for are always detached and kept as standalone events, whichever scope is requested."
                ),
                ["POST /api/events/{eventId}/series/detach"] = new(
                    "Detach an occurrence from its series",
                    "Leaves the event in place as an ordinary standalone event, excluded from future series-wide updates."
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

                // ── Event favourites ─────────────────────────────────────────────────────

                ["POST /api/events/{eventId}/favourite"] = new(
                    "Save an event to favourites",
                    "Stars an event without registering for it — no seat is consumed. Idempotent: starring an already-starred event succeeds rather than conflicting."
                ),
                ["DELETE /api/events/{eventId}/favourite"] = new(
                    "Remove an event from favourites",
                    "Idempotent: unstarring an event that is not starred succeeds."
                ),
                ["GET /api/events/{eventId}/favourite/me"] = new("Check current user favourite status"),
                ["GET /api/events/me/favourites/ids"] = new(
                    "List the current user's favourited event IDs",
                    "Returns just the IDs so list views can render star state in one request without enriching every event payload."
                ),
                ["GET /api/events/me/pinned"] = new(
                    "List the current user's pinned events",
                    "The union of the events the user registered for and the events they starred, ordered registered-first then by start time. Rows the user can no longer view are returned redacted with `accessRevoked` set, so the star can still be removed."
                ),

                // ── Payments ─────────────────────────────────────────────────────────────

                ["POST /api/payments/{eventId}"] = new("Create a Stripe checkout session"),
                ["GET /api/payments/{paymentId}"] = new("Get a payment"),
                ["GET /api/payments/me"] = new("List the current user's payments"),
                ["POST /api/payments/webhook"] = new(
                    "Handle a Stripe webhook event",
                    "Validates and processes a Stripe webhook event using the raw request payload and `Stripe-Signature` header. The endpoint must receive the unmodified raw body."
                ),
                ["POST /api/payments/{paymentId}/refund"] = new("Refund a payment"),

                // ── Profile ───────────────────────────────────────────────────────────────

                ["GET /api/profile"] = new("Get the current user's profile"),
                ["GET /api/profile/{username}"] = new(
                    "Get a public profile by username",
                    "Active previous-username reservations resolve to the owning user's current profile. The response always contains the canonical current username."
                ),
                ["PATCH /api/profile"] = new(
                    "Update the current user's profile",
                    "Updates ordinary profile fields. Username changes use the dedicated MFA-protected endpoint."
                ),
                ["PATCH /api/profile/username"] = new(
                    "Change the current user's username",
                    "Requires recent MFA verification. Usernames are trimmed and lowercased; replacing an existing username starts the configured cooldown and reserves the previous username for the same period."
                ),
                ["POST /api/profile/avatar"] = new("Update the current user's avatar"),
                ["POST /api/profile/change-password"] = new("Change the current user's password"),
                ["DELETE /api/profile"] = new("Delete the current user's account"),

                // ── Users ────────────────────────────────────────────────────────────────

                ["GET /api/users/{userId}/clubs/following"] = new("List clubs followed by a user"),
                ["GET /api/users/{userId}/events/registered"] = new("List events a user is registered for"),
                ["GET /api/users/{userId}/reviews"] = new("List reviews written by a user"),
            };
    }

    internal readonly record struct OperationMeta(string Summary, string? Description = null);
}


