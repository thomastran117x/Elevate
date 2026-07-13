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
