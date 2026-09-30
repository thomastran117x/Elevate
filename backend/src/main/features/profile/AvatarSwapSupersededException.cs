using backend.main.shared.exceptions.http;

namespace backend.main.features.profile;

/// <summary>
/// An avatar swap that had already attempted its commit, then found on a re-run that another
/// upload owns the avatar. It refuses to write again rather than point the account at a blob that
/// other request has deleted.
/// </summary>
/// <remarks>
/// A <see cref="ConflictException"/>, and deliberately the same error code: to a client this is the
/// same retryable "someone else changed it" condition as an exhausted swap, and a caller deciding
/// whether to retry should not have to know two codes for one situation.
/// <para>
/// <see cref="ReplacedAvatarUrl"/> is what the attempt read before writing, which the caller has to
/// clean up. If that write did commit, this is the only report of that URL anyone gets, so without
/// the delete it stays in a public container for good — <c>OrphanBlobCleanup</c> is opt-in. If the
/// write rolled back instead, the request that overtook it has already reported and deleted the
/// same URL, so the second delete is a no-op. Either way the account does not point at it.
/// </para>
/// </remarks>
public sealed class AvatarSwapSupersededException : ConflictException
{
    /// <summary>What both this and the exhausted-retry conflict tell the user.</summary>
    public const string AvatarChangedMessage = "The avatar was changed by another request. Try again.";

    public AvatarSwapSupersededException(string? replacedAvatarUrl)
        : base(AvatarChangedMessage)
    {
        ReplacedAvatarUrl = replacedAvatarUrl;
    }

    public string? ReplacedAvatarUrl
    {
        get;
    }
}
