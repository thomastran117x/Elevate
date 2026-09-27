using backend.main.shared.exceptions.http;

namespace backend.main.features.profile;

/// <summary>
/// An avatar swap whose own commit may or may not have landed, and which another upload has since
/// replaced. The swap refuses to write again rather than point the account at a blob that other
/// request has already deleted.
/// </summary>
/// <remarks>
/// <see cref="ReplacedAvatarUrl"/> is what the ambiguous attempt read before writing. If that
/// attempt did commit, nothing else will ever report that URL, so the caller has to delete it here
/// or it stays in storage referenced by nothing. Deleting it when the attempt actually rolled back
/// is harmless in the same way as any other superseded avatar: the account no longer points at it.
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
