using backend.main.features.auth.device;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Auth;

/// <remarks>
/// SQLite has no row locks, so these pin the comparison and the write rather than the
/// serialization against a concurrent rotation. That half rests on PostgreSQL's
/// <c>FOR UPDATE</c>, the same as <c>AuthUserRepository.ChangeEmailAsync</c>.
/// </remarks>
public class DeviceRepositoryTests
{
    [Fact]
    public async Task TrustIfAuthVersionAsync_ShouldCreateTheDevice_WhenTheVersionMatches()
    {
        await using var harness = await DeviceRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(authVersion: 3);

        var trusted = await harness.Repository.TrustIfAuthVersionAsync(
            userId, 3, NewDevice("device-hash"));

        trusted.Should().BeTrue();
        var stored = await harness.Db.Devices.AsNoTracking().SingleAsync();
        stored.UserId.Should().Be(userId);
        stored.DeviceTokenHash.Should().Be("device-hash");
        stored.ClientName.Should().Be("Chrome");
    }

    [Fact]
    public async Task TrustIfAuthVersionAsync_ShouldRefreshAnExistingDevice_WithoutDuplicatingIt()
    {
        await using var harness = await DeviceRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(authVersion: 3);
        await harness.Repository.TrustIfAuthVersionAsync(userId, 3, NewDevice("device-hash"));

        var refreshed = NewDevice("device-hash");
        refreshed.ClientName = "Firefox";
        refreshed.IpAddress = "10.0.0.2";
        var trusted = await harness.Repository.TrustIfAuthVersionAsync(userId, 3, refreshed);

        trusted.Should().BeTrue();
        var stored = await harness.Db.Devices.AsNoTracking().SingleAsync();
        stored.ClientName.Should().Be("Firefox");
        stored.IpAddress.Should().Be("10.0.0.2");
    }

    /// <summary>
    /// The sign-in this trust belonged to was started before a credential rotation, so it must
    /// not leave behind a device that skips MFA.
    /// </summary>
    [Fact]
    public async Task TrustIfAuthVersionAsync_ShouldWriteNothing_WhenTheVersionMovedOn()
    {
        await using var harness = await DeviceRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(authVersion: 4);

        var trusted = await harness.Repository.TrustIfAuthVersionAsync(
            userId, 3, NewDevice("device-hash"));

        trusted.Should().BeFalse();
        (await harness.Db.Devices.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task TrustIfAuthVersionAsync_ShouldWriteNothing_ForAnUnknownAccount()
    {
        await using var harness = await DeviceRepositoryHarness.CreateAsync();

        var trusted = await harness.Repository.TrustIfAuthVersionAsync(
            9999, 1, NewDevice("device-hash"));

        trusted.Should().BeFalse();
        (await harness.Db.Devices.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    private static Device NewDevice(string deviceTokenHash) => new()
    {
        DeviceTokenHash = deviceTokenHash,
        DeviceType = "Desktop",
        ClientName = "Chrome",
        IpAddress = "127.0.0.1"
    };

    private sealed class DeviceRepositoryHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public DeviceRepository Repository { get; }

        private DeviceRepositoryHarness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;
            Repository = new DeviceRepository(db);
        }

        public static async Task<DeviceRepositoryHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var db = new AppDatabaseContext(
                new DbContextOptionsBuilder<AppDatabaseContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            return new DeviceRepositoryHarness(connection, db);
        }

        public async Task<int> SeedUserAsync(int authVersion)
        {
            var user = new User
            {
                Email = "device@example.com",
                Password = "seed-password",
                Usertype = "participant",
                Name = "Seed User",
                Username = "device-user",
                AuthVersion = authVersion
            };

            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return user.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
