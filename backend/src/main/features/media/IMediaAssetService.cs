using backend.main.features.events.contracts.responses;
using backend.main.shared.storage;

namespace backend.main.features.media;

/// <summary>
/// The single way an uploaded image enters the system: issue a presigned upload, then attach
/// what was uploaded. <c>storage.quarantine</c> picks the implementation — quarantine and
/// validate, or the original public-container upload — so every attach path behaves the same
/// way under either setting.
/// </summary>
public interface IMediaAssetService
{
    /// <summary>
    /// Issues a presigned upload URL. The caller records the upload intent against the returned
    /// <see cref="PresignedUploadResponse.PublicUrl"/>, including its
    /// <see cref="PresignedUploadResponse.MediaAssetId"/>.
    /// </summary>
    Task<PresignedUploadResponse> IssueUploadAsync(
        MediaUploadRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Proves <paramref name="imageUrl"/> is an upload issued to <paramref name="userId"/> and
    /// makes sure an acceptable image is published there, returning the upload's intent. Throws
    /// a 400 describing the problem when it is not.
    /// </summary>
    /// <param name="userId">The user attaching the image, who must be the one it was issued to.</param>
    /// <param name="imageUrl">The public URL the upload was issued with.</param>
    /// <param name="subject">Names the upload in messages — "Event images" or "Club images".</param>
    /// <param name="checkScope">
    /// The caller's own checks on the intent (the right club, the right event). Run as soon as
    /// the intent is known and, where the implementation allows, before any bytes are processed.
    /// </param>
    /// <param name="cancellationToken">Cancels reading and processing the upload.</param>
    Task<BlobUploadIntent> AttachAsync(
        int userId,
        string imageUrl,
        string subject,
        Action<BlobUploadIntent>? checkScope = null,
        CancellationToken cancellationToken = default);
}

/// <summary>What a presigned upload is for and where it will be stored.</summary>
/// <param name="UserId">The user the upload is issued to.</param>
/// <param name="ClubId">Null for a club that does not exist yet.</param>
/// <param name="EventId">The event the upload is for, when there is one.</param>
/// <param name="BlobPathPrefix">The folder the blob is stored under.</param>
/// <param name="FileName">The client's file name, which decides the stored extension.</param>
/// <param name="ContentType">The content type the client declared.</param>
public sealed record MediaUploadRequest(
    int UserId,
    int? ClubId,
    int? EventId,
    string BlobPathPrefix,
    string FileName,
    string ContentType);
