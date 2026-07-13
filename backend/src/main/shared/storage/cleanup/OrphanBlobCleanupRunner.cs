using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.main.shared.storage.cleanup
{
    /// <summary>
    /// Sweeps the blob container for images no longer referenced by any live row
    /// (User.Avatar, Club.ClubImage, ClubVersion.ClubImage, EventImage.ImageUrl) and
    /// deletes them. Reclaims blobs orphaned by cascade-deleted accounts/clubs whose rows
    /// no longer exist, so a reference-check against surviving rows can't find them inline.
    /// </summary>
    public sealed class OrphanBlobCleanupRunner
    {
        private readonly AppDatabaseContext _db;
        private readonly IAzureBlobService _blobService;
        private readonly OrphanBlobCleanupOptions _options;
        private readonly TimeProvider _timeProvider;

        public OrphanBlobCleanupRunner(
            AppDatabaseContext db,
            IAzureBlobService blobService,
            IOptions<OrphanBlobCleanupOptions> options,
            TimeProvider timeProvider)
        {
            _db = db;
            _blobService = blobService;
            _options = options.Value;
            _timeProvider = timeProvider;
        }

        public async Task RunOnceAsync(CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled || _options.BatchSize <= 0 || _options.MinAgeHours <= 0)
                return;

            var cutoff = _timeProvider.GetUtcNow().AddHours(-_options.MinAgeHours);
            var deleted = 0;

            foreach (var prefix in _options.Prefixes)
            {
                await foreach (var blob in _blobService.ListBlobsAsync(prefix, cancellationToken))
                {
                    if (deleted >= _options.BatchSize)
                        return;

                    // Never touch blobs younger than the safety cutoff — they may be a
                    // freshly uploaded image whose URL has not been persisted yet.
                    if (blob.LastModified is null || blob.LastModified > cutoff)
                        continue;

                    if (await IsReferencedAsync(blob.Url, cancellationToken))
                        continue;

                    await _blobService.DeleteBlobAsync(blob.Url);
                    deleted++;
                }
            }
        }

        private async Task<bool> IsReferencedAsync(string url, CancellationToken cancellationToken)
        {
            if (await _db.Users.AsNoTracking().AnyAsync(u => u.Avatar == url, cancellationToken))
                return true;

            if (await _db.Clubs.AsNoTracking().AnyAsync(c => c.ClubImage == url, cancellationToken))
                return true;

            if (await _db.ClubVersions.AsNoTracking().AnyAsync(v => v.ClubImage == url, cancellationToken))
                return true;

            if (await _db.EventImages.AsNoTracking().AnyAsync(i => i.ImageUrl == url, cancellationToken))
                return true;

            return false;
        }
    }
}
