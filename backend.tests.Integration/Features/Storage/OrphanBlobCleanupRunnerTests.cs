using backend.main.shared.storage.cleanup;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.Extensions.Options;

namespace backend.tests.Integration.Features.Storage;

public class OrphanBlobCleanupRunnerTests
{
    [Fact]
    public async Task RunOnceAsync_DeletesOnlyUnreferencedBlobsOlderThanCutoff()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("sweeper@example.com");

        var old = DateTimeOffset.UtcNow.AddDays(-2);
        var recent = DateTimeOffset.UtcNow;

        // Referenced by a live user row — must survive even though it is old.
        var referencedUrl = app.BlobStorage.CreateOwnedBlobUrl("users", "ref.png", old);
        await app.QueryDbAsync(async db =>
        {
            var owner = await db.Users.FindAsync(user.Id);
            owner!.Avatar = referencedUrl;
            await db.SaveChangesAsync();
            return true;
        });

        // Unreferenced + old — the orphan the sweeper should reclaim.
        var orphanOldUrl = app.BlobStorage.CreateOwnedBlobUrl("users", "orphan-old.png", old);
        // Unreferenced + recent — must survive, protected by the age cutoff.
        var orphanRecentUrl = app.BlobStorage.CreateOwnedBlobUrl("users", "orphan-recent.png", recent);

        await app.QueryDbAsync(async db =>
        {
            var runner = new OrphanBlobCleanupRunner(
                db,
                app.BlobStorage,
                Options.Create(new OrphanBlobCleanupOptions
                {
                    Enabled = true,
                    MinAgeHours = 24,
                    BatchSize = 200,
                    Prefixes = ["users"]
                }),
                TimeProvider.System);
            await runner.RunOnceAsync();
            return true;
        });

        app.BlobStorage.IsOwnedBlobUrl(referencedUrl).Should().BeTrue("it is still referenced by a live user");
        app.BlobStorage.IsOwnedBlobUrl(orphanRecentUrl).Should().BeTrue("it is younger than the safety cutoff");
        app.BlobStorage.IsOwnedBlobUrl(orphanOldUrl).Should().BeFalse("it is an unreferenced, aged orphan");
    }

    [Fact]
    public async Task RunOnceAsync_WhenDisabled_DeletesNothing()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var orphanUrl = app.BlobStorage.CreateOwnedBlobUrl("users", "orphan.png", DateTimeOffset.UtcNow.AddDays(-2));

        await app.QueryDbAsync(async db =>
        {
            var runner = new OrphanBlobCleanupRunner(
                db,
                app.BlobStorage,
                Options.Create(new OrphanBlobCleanupOptions { Enabled = false, Prefixes = ["users"] }),
                TimeProvider.System);
            await runner.RunOnceAsync();
            return true;
        });

        app.BlobStorage.IsOwnedBlobUrl(orphanUrl).Should().BeTrue("the sweeper is disabled");
    }
}
