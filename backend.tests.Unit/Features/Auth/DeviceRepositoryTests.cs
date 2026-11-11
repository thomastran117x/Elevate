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
        refreshed.ExpiresAt = Now.AddDays(120);
        var trusted = await harness.Repository.TrustIfAuthVersionAsync(userId, 3, refreshed);

        trusted.Should().BeTrue();
        var stored = await harness.Db.Devices.AsNoTracking().SingleAsync();
        stored.ClientName.Should().Be("Firefox");
        stored.IpAddress.Should().Be("10.0.0.2");
        // Verifying again renews the trust.
        stored.ExpiresAt.Should().Be(Now.AddDays(120));
    }

    [Fact]
    public async Task ListActiveAsync_ShouldReturnOnlyTheAccountsUnexpiredDevices_MostRecentFirst()
    {
        await using var harness = await DeviceRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(authVersion: 1);
        var otherId = await harness.SeedUserAsync(authVersion: 1, username: "other-device-user");
        await harness.AddAsync(userId, "older", lastSeenAt: Now.AddDays(-3), expiresAt: Now.AddDays(10));
        await harness.AddAsync(userId, "newer", lastSeenAt: Now.AddDays(-1), expiresAt: Now.AddDays(10));
        await harness.AddAsync(userId, "expired", lastSeenAt: Now, expiresAt: Now);
        await harness.AddAsync(otherId, "theirs", lastSeenAt: Now, expiresAt: Now.AddDays(10));

        var devices = await harness.Repository.ListActiveAsync(userId, Now);

        devices.Select(d => d.DeviceTokenHash).Should().Equal("newer", "older");
    }

    [Fact]
    public async Task DeleteAsync_ShouldOnlyRemoveTheAccountsOwnDevice()
    {
        await using var harness = await DeviceRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(authVersion: 1);
        var otherId = await harness.SeedUserAsync(authVersion: 1, username: "other-device-user");
        var deviceId = await harness.AddAsync(userId, "mine", lastSeenAt: Now, expiresAt: Now.AddDays(10));

        (await harness.Repository.DeleteAsync(otherId, deviceId)).Should().BeFalse();
        (await harness.Db.Devices.AsNoTracking().AnyAsync()).Should().BeTrue();

        (await harness.Repository.DeleteAsync(userId, deviceId)).Should().BeTrue();
        (await harness.Db.Devices.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAllAsync_ShouldRemoveEveryDeviceOfTheAccount_AndNoOther()
    {
        await using var harness = await DeviceRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(authVersion: 1);
        var otherId = await harness.SeedUserAsync(authVersion: 1, username: "other-device-user");
        await harness.AddAsync(userId, "laptop", lastSeenAt: Now, expiresAt: Now.AddDays(10));
        await harness.AddAsync(userId, "phone", lastSeenAt: Now, expiresAt: Now.AddDays(10));
        await harness.AddAsync(otherId, "theirs", lastSeenAt: Now, expiresAt: Now.AddDays(10));

        (await harness.Repository.DeleteAllAsync(userId)).Should().Be(2);

        (await harness.Db.Devices.AsNoTracking().Select(d => d.DeviceTokenHash).ToListAsync())
            .Should().Equal("theirs");
    }

    [Fact]
    public async Task DeleteExpiredAsync_ShouldRemoveOnlyDevicesWhoseTrustEnded_InBatches()
    {
        await using var harness = await DeviceRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(authVersion: 1);
        await harness.AddAsync(userId, "ended", lastSeenAt: Now.AddDays(-91), expiresAt: Now.AddDays(-1));
        await harness.AddAsync(userId, "ends-now", lastSeenAt: Now.AddDays(-90), expiresAt: Now);
        await harness.AddAsync(userId, "live", lastSeenAt: Now, expiresAt: Now.AddDays(1));

        (await harness.Repository.DeleteExpiredAsync(Now, batchSize: 1)).Should().Be(1);
        (await harness.Repository.DeleteExpiredAsync(Now, batchSize: 10)).Should().Be(1);
        (await harness.Repository.DeleteExpiredAsync(Now, batchSize: 10)).Should().Be(0);

        (await harness.Db.Devices.AsNoTracking().Select(d => d.DeviceTokenHash).ToListAsync())
            .Should().Equal("live");
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

    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    private static Device NewDevice(string deviceTokenHash) => new()
    {
        DeviceTokenHash = deviceTokenHash,
        DeviceType = "Desktop",
        ClientName = "Chrome",
        IpAddress = "127.0.0.1",
        ExpiresAt = Now.AddDays(90),
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

        public async Task<int> SeedUserAsync(int authVersion, string username = "device-user")
        {
            var user = new User
            {
                Email = $"{username}@example.com",
                Password = "seed-password",
                Usertype = "participant",
                Name = "Seed User",
                Username = username,
                AuthVersion = authVersion
            };

            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return user.Id;
        }

        public async Task<int> AddAsync(int userId, string deviceTokenHash, DateTime lastSeenAt, DateTime expiresAt)
        {
            var device = NewDevice(deviceTokenHash);
            device.UserId = userId;
            device.LastSeenAt = lastSeenAt;
            device.ExpiresAt = expiresAt;
            Db.Devices.Add(device);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return device.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
