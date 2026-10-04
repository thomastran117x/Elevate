using backend.main.shared.exceptions.http;

namespace backend.main.features.media;

/// <summary>
/// An attached upload has not been validated yet. Not a refusal: the asset keeps being worked on,
/// so a client that sees <see cref="Code"/> can follow it with <c>GET /api/media/{publicId}</c>
/// and attach it again once it is Ready, rather than discarding it as it would a rejection.
/// </summary>
public sealed class MediaStillProcessingException : ConflictException
{
    public const string Code = "MEDIA_PROCESSING";

    public MediaStillProcessingException()
        : base(MediaAssetService.StillProcessingMessage, Code, null)
    {
    }
}
