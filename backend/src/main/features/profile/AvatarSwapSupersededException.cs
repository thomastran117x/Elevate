using backend.main.shared.exceptions.http;

namespace backend.main.features.profile;

/// <summary>
/// An avatar swap that had already issued its write, then found on a re-run that another upload
/// owns the avatar. It refuses to write again rather than point the account at a blob that other
/// request has deleted.
/// </summary>
/// <remarks>
/// <see cref="ReplacedAvatarUrl"/> is what the attempt read before writing. If that write did
/// commit, this is the only report of that URL anyone gets, so the caller has to delete it or it
/// stays in a public container for good — <c>OrphanBlobCleanup</c> is opt-in. If the write rolled
/// back instead, the request that overtook it has already reported and deleted the same URL, so
/// the second delete is a no-op. Either way the account does not point at it.
/// </remarks>
public sealed class AvatarSwapSupersededException : AppException
{
    public AvatarSwapSupersededException(string? replacedAvatarUrl)
        : base(
            "The avatar was changed by another request. Try again.",
            StatusCodes.Status409Conflict,
            "AVATAR_SWAP_SUPERSEDED")
    {
        ReplacedAvatarUrl = replacedAvatarUrl;
    }

    public string? ReplacedAvatarUrl
    {
        get;
    }
}
