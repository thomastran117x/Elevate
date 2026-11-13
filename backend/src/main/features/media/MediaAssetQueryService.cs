using backend.main.application.security.authorization;
using backend.main.features.media.contracts.responses;
using backend.main.shared.exceptions.http;

namespace backend.main.features.media;

public interface IMediaAssetQueryService
{
    /// <summary>
    /// The asset's status for someone allowed to see it: its uploader, or a manager of the club
    /// it was uploaded for.
    /// </summary>
    /// <exception cref="ResourceNotFoundException">
    /// The asset does not exist, or the viewer may not see it. The two are deliberately
    /// indistinguishable, so the endpoint cannot be used to probe which ids exist.
    /// </exception>
    Task<MediaAssetResponse> GetForViewerAsync(
        Guid publicId,
        int userId,
        string? userRole,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Read side of the media lifecycle. Separate from <see cref="IMediaAssetService"/> because it
/// needs the club manager check (<c>media.view</c>), and the club service already depends on the
/// upload service.
/// </summary>
public sealed class MediaAssetQueryService : IMediaAssetQueryService
{
    private readonly IMediaAssetRepository _repository;
    private readonly IResourceAuthorizer _authorizer;

    public MediaAssetQueryService(IMediaAssetRepository repository, IResourceAuthorizer authorizer)
    {
        _repository = repository;
        _authorizer = authorizer;
    }

    public async Task<MediaAssetResponse> GetForViewerAsync(
        Guid publicId,
        int userId,
        string? userRole,
        CancellationToken cancellationToken = default)
    {
        var asset = await _repository.GetByPublicIdAsync(publicId, cancellationToken);
        if (asset == null || !await _authorizer.IsAllowedAsync(
                ResourceActor.User(userId, userRole),
                Permissions.MediaView,
                new MediaAssetResource(publicId),
                cancellationToken))
        {
            throw new ResourceNotFoundException("Media asset not found.");
        }

        return new MediaAssetResponse
        {
            Id = asset.PublicId,
            Status = asset.Status,
            Url = asset.Status == MediaAssetStatus.Ready ? asset.PublicUrl : null,
            RejectionReason = asset.Status == MediaAssetStatus.Rejected ? asset.RejectionReason : null
        };
    }
}
