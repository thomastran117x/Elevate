using backend.main.infrastructure.database.core;
using backend.main.shared.storage;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.main.features.clubs.versions;

public sealed class ClubVersionCleanupRunner
{
    private readonly AppDatabaseContext _db;
    private readonly IFileUploadService _fileUploadService;
    private readonly ClubVersioningOptions _options;
    private readonly TimeProvider _timeProvider;

    public ClubVersionCleanupRunner(
        AppDatabaseContext db,
        IFileUploadService fileUploadService,
        IOptions<ClubVersioningOptions> options,
        TimeProvider timeProvider)
    {
        _db = db;
        _fileUploadService = fileUploadService;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.PurgeEnabled || _options.PurgeBatchSize <= 0 || _options.RollbackWindowDays <= 0)
            return;

        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-_options.RollbackWindowDays);

        var candidateUrls = await _db.ClubVersions
            .AsNoTracking()
            .Where(v =>
                v.CreatedAt < cutoff &&
                v.ClubImage != null &&
                v.ClubImage != string.Empty)
            .OrderBy(v => v.CreatedAt)
            .Select(v => v.ClubImage!)
            .Distinct()
            .Take(_options.PurgeBatchSize)
            .ToListAsync(cancellationToken);

        foreach (var imageUrl in candidateUrls)
        {
            var referencedByCurrentClub = await _db.Clubs
                .AsNoTracking()
                .AnyAsync(c => c.ClubImage == imageUrl, cancellationToken);

            if (referencedByCurrentClub)
                continue;

            var referencedByRollbackableVersion = await _db.ClubVersions
                .AsNoTracking()
                .AnyAsync(v => v.CreatedAt >= cutoff && v.ClubImage == imageUrl, cancellationToken);

            if (referencedByRollbackableVersion)
                continue;

            await _fileUploadService.DeleteImageAsync(imageUrl);
        }
    }
}
