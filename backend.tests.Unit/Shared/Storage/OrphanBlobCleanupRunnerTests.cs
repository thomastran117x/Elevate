using backend.main.features.media;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.shared.storage;
using backend.main.shared.storage.cleanup;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Shared.Storage;

public class OrphanBlobCleanupRunnerTests
{
    private const string ReferencedUrl = "https://cdn.test/users/referenced.png";
    private const string OrphanOldUrl = "https://cdn.test/users/orphan-old.png";
    private const string OrphanRecentUrl = "https://cdn.test/users/orphan-recent.png";

    [Fact]
    public async Task RunOnceAsync_DeletesOnlyUnreferencedBlobsOlderThanCutoff()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SeedUserWithAvatarAsync(ReferencedUrl);

        var old = DateTimeOffset.UtcNow.AddDays(-2);
        var recent = DateTimeOffset.UtcNow;
        harness.BlobService
            .Setup(b => b.ListBlobsAsync("users", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(
                new BlobListItem(ReferencedUrl, old),
                new BlobListItem(OrphanOldUrl, old),
                new BlobListItem(OrphanRecentUrl, recent)));

        await harness.CreateRunner(prefixes: ["users"]).RunOnceAsync();

        harness.BlobService.Verify(b => b.DeleteBlobAsync(OrphanOldUrl), Times.Once);
        harness.BlobService.Verify(b => b.DeleteBlobAsync(ReferencedUrl), Times.Never);
        harness.BlobService.Verify(b => b.DeleteBlobAsync(OrphanRecentUrl), Times.Never);
    }

    [Theory]
    [InlineData(MediaAssetStatus.PendingUpload)]
    [InlineData(MediaAssetStatus.Uploaded)]
    [InlineData(MediaAssetStatus.Processing)]
    [InlineData(MediaAssetStatus.NeedsReview)]
    public async Task RunOnceAsync_ShouldProtectThePublicUrlOfAnUploadStillInFlight(MediaAssetStatus status)
    {
        // A crash between promotion and the Ready write leaves a public blob under an asset that
        // is still Processing; the retry that finishes it must find the blob there.
        await using var harness = await Harness.CreateAsync();
        const string inFlightUrl = "https://cdn.test/users/in-flight.webp";
        await harness.SeedAssetAsync(inFlightUrl, status);

        var old = DateTimeOffset.UtcNow.AddDays(-2);
        harness.BlobService
            .Setup(b => b.ListBlobsAsync("users", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(new BlobListItem(inFlightUrl, old)));

        await harness.CreateRunner(prefixes: ["users"]).RunOnceAsync();

        harness.BlobService.Verify(b => b.DeleteBlobAsync(inFlightUrl), Times.Never);
    }

    [Theory]
    [InlineData(MediaAssetStatus.Ready)]
    [InlineData(MediaAssetStatus.Rejected)]
    public async Task RunOnceAsync_ShouldReclaimASettledUploadNothingReferences_AndRetireItsRow(MediaAssetStatus status)
    {
        // An image removed from its event whose inline delete failed, or one promoted by an attach
        // whose save then failed: only the ledger still knows the URL. Treating that as a reference
        // would keep the blob forever, which is the one case the sweeper exists for.
        await using var harness = await Harness.CreateAsync();
        const string settledUrl = "https://cdn.test/users/removed.webp";
        await harness.SeedAssetAsync(settledUrl, status);

        var old = DateTimeOffset.UtcNow.AddDays(-2);
        harness.BlobService
            .Setup(b => b.ListBlobsAsync("users", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(new BlobListItem(settledUrl, old)));

        await harness.CreateRunner(prefixes: ["users"]).RunOnceAsync();

        harness.BlobService.Verify(b => b.DeleteBlobAsync(settledUrl), Times.Once);
        (await harness.Db.MediaAssets.AsNoTracking().AnyAsync(a => a.PublicUrl == settledUrl))
            .Should().BeFalse("no Ready row should advertise a URL with nothing behind it");
    }

    [Fact]
    public async Task RunOnceAsync_ShouldKeepASettledUploadThatIsStillAttached()
    {
        await using var harness = await Harness.CreateAsync();
        const string attachedUrl = "https://cdn.test/users/attached.webp";
        await harness.SeedAssetAsync(attachedUrl, MediaAssetStatus.Ready);
        await harness.SeedUserWithAvatarAsync(attachedUrl);

        var old = DateTimeOffset.UtcNow.AddDays(-2);
        harness.BlobService
            .Setup(b => b.ListBlobsAsync("users", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(new BlobListItem(attachedUrl, old)));

        await harness.CreateRunner(prefixes: ["users"]).RunOnceAsync();

        harness.BlobService.Verify(b => b.DeleteBlobAsync(attachedUrl), Times.Never);
        (await harness.Db.MediaAssets.AsNoTracking().AnyAsync(a => a.PublicUrl == attachedUrl)).Should().BeTrue();
    }

    [Fact]
    public async Task RunOnceAsync_WhenDisabled_DeletesNothing()
    {
        await using var harness = await Harness.CreateAsync();

        harness.BlobService
            .Setup(b => b.ListBlobsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(new BlobListItem(OrphanOldUrl, DateTimeOffset.UtcNow.AddDays(-2))));

        await harness.CreateRunner(prefixes: ["users"], enabled: false).RunOnceAsync();

        harness.BlobService.Verify(b => b.DeleteBlobAsync(It.IsAny<string>()), Times.Never);
        harness.BlobService.Verify(b => b.ListBlobsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_StopsAfterBatchSize()
    {
        await using var harness = await Harness.CreateAsync();

        var old = DateTimeOffset.UtcNow.AddDays(-2);
        harness.BlobService
            .Setup(b => b.ListBlobsAsync("users", It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(
                new BlobListItem("https://cdn.test/users/a.png", old),
                new BlobListItem("https://cdn.test/users/b.png", old),
                new BlobListItem("https://cdn.test/users/c.png", old)));

        await harness.CreateRunner(prefixes: ["users"], batchSize: 2).RunOnceAsync();

        harness.BlobService.Verify(b => b.DeleteBlobAsync(It.IsAny<string>()), Times.Exactly(2));
    }

    private static async IAsyncEnumerable<BlobListItem> ToAsyncEnumerable(params BlobListItem[] items)
    {
        foreach (var item in items)
            yield return item;

        await Task.CompletedTask;
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public Mock<IAzureBlobService> BlobService { get; } = new();

        private Harness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();

            return new Harness(connection, db);
        }

        public async Task SeedAssetAsync(string publicUrl, MediaAssetStatus status)
        {
            Db.MediaAssets.Add(new MediaAsset
            {
                PublicId = Guid.NewGuid(),
                Status = status,
                Origin = MediaAssetOrigin.Upload,
                PublicUrl = publicUrl,
                DeclaredContentType = "image/png"
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task SeedUserWithAvatarAsync(string avatarUrl)
        {
            Db.Users.Add(new User
            {
                Email = "seed@example.com",
                Password = "seed-password",
                Usertype = "participant",
                Username = "seed-user",
                Avatar = avatarUrl
            });
            await Db.SaveChangesAsync();
        }

        public OrphanBlobCleanupRunner CreateRunner(
            string[] prefixes,
            bool enabled = true,
            int batchSize = 200,
            int minAgeHours = 24)
        {
            var options = Options.Create(new OrphanBlobCleanupOptions
            {
                Enabled = enabled,
                BatchSize = batchSize,
                MinAgeHours = minAgeHours,
                Prefixes = prefixes
            });

            return new OrphanBlobCleanupRunner(Db, BlobService.Object, options, TimeProvider.System);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
