using backend.main.features.bloom;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Bloom;

public class UsernameBloomFilterSourceTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Target_ShouldBeTheUsernameFilter()
    {
        new UsernameBloomFilterSource(null!, TimeProvider.System).Target
            .Should().Be(BloomFilterTargets.Username);
    }

    [Fact]
    public async Task EnumerateAsync_ShouldYieldEveryLiveUsername()
    {
        await using var harness = await SourceHarness.CreateAsync();
        await harness.AddUserAsync("ada@example.com", "ada");
        await harness.AddUserAsync("grace@example.com", "grace");

        var values = await harness.EnumerateAsync();

        values.Should().BeEquivalentTo(["ada", "grace"]);
    }

    /// <summary>
    /// A released username sits in the reservation cooldown table and is still unavailable, so
    /// the filter must cover it or it would report a reserved name as free.
    /// </summary>
    [Fact]
    public async Task EnumerateAsync_ShouldIncludeActiveReservations()
    {
        await using var harness = await SourceHarness.CreateAsync();
        await harness.AddUserAsync("ada@example.com", "ada");
        await harness.AddReservationAsync("previous-name", Now.AddDays(10));

        var values = await harness.EnumerateAsync();

        values.Should().BeEquivalentTo(["ada", "previous-name"]);
    }

    [Fact]
    public async Task EnumerateAsync_ShouldExcludeExpiredReservations()
    {
        // An expired reservation no longer blocks a signup, so carrying it would keep a freed
        // name looking taken until the next rebuild.
        await using var harness = await SourceHarness.CreateAsync();
        await harness.AddReservationAsync("released", Now.AddDays(-1));

        var values = await harness.EnumerateAsync();

        values.Should().BeEmpty();
    }

    [Fact]
    public async Task EnumerateAsync_ShouldSkipUsersWithoutAUsername()
    {
        await using var harness = await SourceHarness.CreateAsync();
        await harness.AddUserAsync("oauth@example.com", username: null);

        var values = await harness.EnumerateAsync();

        values.Should().BeEmpty();
    }

    [Fact]
    public async Task EnumerateAsync_ShouldNormaliseValues_SoTheyMatchLookups()
    {
        await using var harness = await SourceHarness.CreateAsync();
        await harness.AddUserAsync("ada@example.com", "  AdaLovelace  ");

        var values = await harness.EnumerateAsync();

        values.Should().BeEquivalentTo(["adalovelace"]);
    }

    [Fact]
    public async Task EnumerateAsync_ShouldReturnNothing_ForAnEmptyDatabase()
    {
        await using var harness = await SourceHarness.CreateAsync();

        (await harness.EnumerateAsync()).Should().BeEmpty();
    }

    private sealed class SourceHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly AppDatabaseContext _db;
        private int _nextUserId = 1;

        private SourceHarness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            _db = db;
            Source = new UsernameBloomFilterSource(db, new FixedTimeProvider(Now));
        }

        public UsernameBloomFilterSource Source { get; }

        public static async Task<SourceHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();

            return new SourceHarness(connection, db);
        }

        public async Task AddUserAsync(string email, string? username)
        {
            _db.Users.Add(new User
            {
                Id = _nextUserId++,
                Email = email,
                Password = "hashed",
                Usertype = "participant",
                Username = username,
            });

            await _db.SaveChangesAsync();
        }

        public async Task AddReservationAsync(string username, DateTime reservedUntilUtc)
        {
            // A reservation is owned by the account that released the name, so it needs a real user row.
            var ownerId = _nextUserId;
            await AddUserAsync("owner-" + ownerId + "@example.com", username: null);

            _db.UsernameReservations.Add(new UsernameReservation
            {
                Username = username,
                UserId = ownerId,
                ReservedUntilUtc = reservedUntilUtc,
            });

            await _db.SaveChangesAsync();
        }

        public async Task<List<string>> EnumerateAsync()
        {
            var values = new List<string>();
            await foreach (var value in Source.EnumerateAsync(CancellationToken.None))
                values.Add(value);

            return values;
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
