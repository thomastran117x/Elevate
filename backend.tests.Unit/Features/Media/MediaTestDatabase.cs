using backend.main.features.clubs;
using backend.main.features.media;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Media;

/// <summary>
/// An in-memory SQLite <see cref="AppDatabaseContext"/> built from the real model, shared by the
/// media tests so the conditional-update behaviour they depend on is exercised against a real
/// query provider rather than a mock.
/// </summary>
internal sealed class MediaTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public AppDatabaseContext Db
    {
        get;
    }

    public MutableTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));

    private MediaTestDatabase(SqliteConnection connection, AppDatabaseContext db)
    {
        _connection = connection;
        Db = db;
    }

    public static async Task<MediaTestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(connection)
            .Options;

        var db = new AppDatabaseContext(options);
        await db.Database.EnsureCreatedAsync();

        return new MediaTestDatabase(connection, db);
    }

    public MediaAssetRepository CreateRepository() => new(Db, Time);

    public async Task<User> SeedUserAsync(string username = "uploader")
    {
        var user = new User
        {
            Email = $"{username}@example.com",
            Password = "seed-password",
            Usertype = "Organizer",
            Username = username
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        return user;
    }

    public async Task<Club> SeedClubAsync(int ownerId, string name = "Media Club")
    {
        var club = new Club
        {
            Name = name,
            Description = "A club for media tests.",
            Clubtype = default,
            ClubImage = "https://storage.test/event-assets/clubs/icon.webp",
            UserId = ownerId
        };
        Db.Clubs.Add(club);
        await Db.SaveChangesAsync();
        return club;
    }

    public async Task<MediaAsset> SeedAssetAsync(
        MediaAssetStatus status = MediaAssetStatus.PendingUpload,
        int? ownerUserId = null,
        int? clubId = null,
        string? publicUrl = null,
        string? quarantineBlobPath = null,
        DateTime? createdAt = null)
    {
        var now = createdAt ?? Time.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        var asset = new MediaAsset
        {
            PublicId = id,
            OwnerUserId = ownerUserId,
            ClubId = clubId,
            Status = status,
            Origin = MediaAssetOrigin.Upload,
            PublicUrl = publicUrl ?? $"https://storage.test/event-assets/events/{id:N}.webp",
            QuarantineBlobPath = quarantineBlobPath ?? $"events/{id:N}.png",
            DeclaredContentType = "image/png",
            CreatedAt = now,
            UpdatedAt = now
        };
        Db.MediaAssets.Add(asset);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return asset;
    }

    /// <summary>The row as stored, bypassing anything the context is tracking.</summary>
    public Task<MediaAsset> ReloadAsync(int id) =>
        Db.MediaAssets.AsNoTracking().SingleAsync(asset => asset.Id == id);

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
