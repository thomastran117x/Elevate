using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.main.shared.storage.cleanup
{
    /// <summary>
    /// Sweeps the blob container for images no longer referenced by any live row
    /// (User.Avatar, Club.ClubImage, Club.BannerImage, Club.GalleryImages,
    /// ClubVersion.ClubImage, EventImage.ImageUrl) and deletes them. Reclaims blobs orphaned by
    /// cascade-deleted accounts/clubs whose rows no longer exist, so a reference-check against
    /// surviving rows can't find them inline.
    /// <para>
    /// Every column holding a blob URL must be listed in <c>IsReferencedAsync</c>. A column left
    /// out is not a missed reclaim — it is deletion of a live image once the sweeper is enabled.
    /// </para>
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

            var galleryUrls = await LoadClubGalleryUrlsAsync(cancellationToken);

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

                    if (galleryUrls.Contains(blob.Url) ||
                        await IsReferencedAsync(blob.Url, cancellationToken))
                    {
                        continue;
                    }

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

            if (await _db.Clubs.AsNoTracking().AnyAsync(c => c.BannerImage == url, cancellationToken))
                return true;

            if (await _db.ClubVersions.AsNoTracking().AnyAsync(v => v.ClubImage == url, cancellationToken))
                return true;

            if (await _db.EventImages.AsNoTracking().AnyAsync(i => i.ImageUrl == url, cancellationToken))
                return true;

            return false;
        }

        /// <summary>
        /// Every club gallery URL, read once per run into a set.
        /// </summary>
        /// <remarks>
        /// Club.GalleryImages is a List&lt;string&gt; behind a JSON value converter, so there is no
        /// column to write a translatable Contains against — the lists have to be materialized and
        /// matched in memory. Loading them once is also cheaper than the per-blob queries around
        /// it: a sweep touches far more blobs than the club table has rows, and each club holds at
        /// most five URLs.
        /// </remarks>
        private async Task<HashSet<string>> LoadClubGalleryUrlsAsync(CancellationToken cancellationToken)
        {
            var galleries = await _db.Clubs
                .AsNoTracking()
                .Select(club => club.GalleryImages)
                .ToListAsync(cancellationToken);

            var urls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var gallery in galleries)
            {
                if (gallery == null)
                    continue;

                foreach (var url in gallery)
                    urls.Add(url);
            }

            return urls;
        }
    }
}
