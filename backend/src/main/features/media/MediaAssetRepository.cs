using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.media;

public class MediaAssetRepository : IMediaAssetRepository
{
    private readonly AppDatabaseContext _context;
    private readonly TimeProvider _timeProvider;

    public MediaAssetRepository(AppDatabaseContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task AddAsync(MediaAsset asset) => await _context.MediaAssets.AddAsync(asset);

    public async Task<MediaAsset?> GetByPublicIdAsync(
        Guid publicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.MediaAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(asset => asset.PublicId == publicId, cancellationToken);
    }

    public async Task<MediaAsset?> GetByQuarantineBlobPathAsync(
        string quarantineBlobPath,
        CancellationToken cancellationToken = default)
    {
        return await _context.MediaAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(asset => asset.QuarantineBlobPath == quarantineBlobPath, cancellationToken);
    }

    public async Task<bool> TryTransitionAsync(
        int id,
        MediaAssetStatus from,
        MediaAssetStatus to,
        MediaAssetChanges? changes = null,
        CancellationToken cancellationToken = default)
    {
        MediaAssetTransitions.EnsureAllowed(from, to);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        changes ??= new MediaAssetChanges();

        var affected = await _context.MediaAssets
            .Where(asset => asset.Id == id && asset.Status == from)
            .ExecuteUpdateAsync(
                setters =>
                {
                    setters.SetProperty(asset => asset.Status, to);
                    setters.SetProperty(asset => asset.UpdatedAt, now);

                    if (changes.RejectionReason != null)
                        setters.SetProperty(asset => asset.RejectionReason, changes.RejectionReason);
                    if (changes.ContentType != null)
                        setters.SetProperty(asset => asset.ContentType, changes.ContentType);
                    if (changes.Width != null)
                        setters.SetProperty(asset => asset.Width, changes.Width);
                    if (changes.Height != null)
                        setters.SetProperty(asset => asset.Height, changes.Height);
                    if (changes.ByteSize != null)
                        setters.SetProperty(asset => asset.ByteSize, changes.ByteSize);
                    if (changes.ValidatedAt != null)
                        setters.SetProperty(asset => asset.ValidatedAt, changes.ValidatedAt);
                    if (changes.ClearQuarantineBlobPath)
                        setters.SetProperty(asset => asset.QuarantineBlobPath, (string?)null);
                    if (changes.CountAttempt)
                        setters.SetProperty(asset => asset.AttemptCount, asset => asset.AttemptCount + 1);
                },
                cancellationToken);

        return affected == 1;
    }

    public async Task<List<MediaAsset>> GetUnattachedIssuedBeforeAsync(
        DateTime createdBefore,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _context.MediaAssets
            .AsNoTracking()
            .Where(asset =>
                (asset.Status == MediaAssetStatus.PendingUpload || asset.Status == MediaAssetStatus.Uploaded) &&
                asset.CreatedAt < createdBefore)
            .OrderBy(asset => asset.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<MediaAsset>> GetProcessingClaimedBeforeAsync(
        DateTime updatedBefore,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _context.MediaAssets
            .AsNoTracking()
            .Where(asset => asset.Status == MediaAssetStatus.Processing && asset.UpdatedAt < updatedBefore)
            .OrderBy(asset => asset.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
