using backend.main.features.clubs;
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
/// needs <see cref="IClubService"/> for the manager check, and the club service already depends on
/// the upload service.
/// </summary>
public sealed class MediaAssetQueryService : IMediaAssetQueryService
{
    private readonly IMediaAssetRepository _repository;
    private readonly IClubService _clubService;

    public MediaAssetQueryService(IMediaAssetRepository repository, IClubService clubService)
    {
        _repository = repository;
        _clubService = clubService;
    }

    public async Task<MediaAssetResponse> GetForViewerAsync(
        Guid publicId,
        int userId,
        string? userRole,
        CancellationToken cancellationToken = default)
    {
        var asset = await _repository.GetByPublicIdAsync(publicId, cancellationToken);
        if (asset == null || !await CanViewAsync(asset, userId, userRole))
            throw new ResourceNotFoundException("Media asset not found.");

        return new MediaAssetResponse
        {
            Id = asset.PublicId,
            Status = asset.Status,
            Url = asset.Status == MediaAssetStatus.Ready ? asset.PublicUrl : null,
            RejectionReason = asset.Status == MediaAssetStatus.Rejected ? asset.RejectionReason : null
        };
    }

    private async Task<bool> CanViewAsync(MediaAsset asset, int userId, string? userRole)
    {
        if (asset.OwnerUserId == userId)
            return true;

        return asset.ClubId is int clubId && await _clubService.CanManageClubAsync(clubId, userId, userRole);
    }
}
