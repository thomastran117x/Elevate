using backend.main.features.auth.events;

namespace backend.main.features.auth.sessions;

public interface IAuthSessionRepository
{
    /// <summary>
    /// Records a use of the session: refreshes its client description, last-seen time, and expiry,
    /// or creates the row when there is none yet. A session from before this table existed is
    /// created on its first refresh this way.
    /// </summary>
    /// <returns>
    /// False, writing nothing, when the row exists but was revoked or has expired, so the caller
    /// must not issue tokens for it.
    /// </returns>
    Task<bool> RecordUseAsync(AuthSession session);

    /// <returns>
    /// The account's state and whether the session is alive, or null when the account is gone.
    /// </returns>
    Task<AccessTokenSessionState?> GetAccessTokenStateAsync(int userId, string sessionId, DateTime now);

    /// <summary>The account's live sessions, most recently used first.</summary>
    Task<List<AuthSession>> ListActiveAsync(int userId, DateTime now);

    /// <returns>
    /// The session's account when this call revoked it, or null when it was unknown or already
    /// revoked.
    /// </returns>
    Task<int?> RevokeAsync(string sessionId, AuthSecurityReason reason, DateTime now);

    /// <summary>
    /// Revokes one of <paramref name="userId"/>'s live sessions. Another account's session, or
    /// one that already ended, is left alone and reported the same way as an unknown id.
    /// </summary>
    Task<bool> RevokeForUserAsync(int userId, string sessionId, AuthSecurityReason reason, DateTime now);

    /// <returns>The sessions this call revoked.</returns>
    Task<IReadOnlyList<string>> RevokeAllAsync(
        int userId,
        AuthSecurityReason reason,
        DateTime now,
        string? exceptSessionId = null);

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> rows that expired before
    /// <paramref name="cutoff"/>, revoked or not.
    /// </summary>
    /// <returns>How many rows were deleted.</returns>
    Task<int> DeleteExpiredAsync(DateTime cutoff, int batchSize);
}
