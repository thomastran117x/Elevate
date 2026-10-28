using backend.main.features.auth.events;
using backend.main.features.auth.history;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Auth.History;

/// <summary>
/// An in-memory SQLite database with two accounts, shared by the security history repository,
/// worker processor, and retention tests.
/// </summary>
internal sealed class SecurityHistoryDbHarness : IAsyncDisposable
{
    public const int OwnerId = 1;
    public const int OtherId = 2;

    private readonly SqliteConnection _connection;

    public AppDatabaseContext Db
    {
        get;
    }

    private SecurityHistoryDbHarness(SqliteConnection connection, AppDatabaseContext db)
    {
        _connection = connection;
        Db = db;
    }

    public static async Task<SecurityHistoryDbHarness> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        // SQLite leaves foreign keys off per connection unless asked, and the deleted-account
        // path depends on the constraint firing.
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync();
        }

        var db = new AppDatabaseContext(CreateOptions(connection));
        await db.Database.EnsureCreatedAsync();

        db.Users.AddRange(
            new User { Id = OwnerId, Email = "owner@test.local", Name = "Owner", Usertype = "Participant" },
            new User { Id = OtherId, Email = "other@test.local", Name = "Other", Usertype = "Participant" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return new SecurityHistoryDbHarness(connection, db);
    }

    /// <summary>A second context on the same database, for code that owns its own context.</summary>
    public AppDatabaseContext CreateContext() => new(CreateOptions(_connection));

    public async Task SeedAsync(params AccountSecurityEvent[] entries)
    {
        Db.AccountSecurityEvents.AddRange(entries);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public static AccountSecurityEvent Entry(
        int userId,
        DateTime occurredAt,
        AccountSecurityEventKind kind = AccountSecurityEventKind.SignedIn,
        DateTime? expiresAt = null) => new()
        {
            MessageId = Guid.NewGuid(),
            UserId = userId,
            Kind = kind,
            EventName = AuthSecurityEvents.SignInSucceeded.Name,
            Method = "password",
            Client = SecurityHistoryClient.Firefox,
            DeviceClass = AuthDeviceClass.Mobile,
            Network = "198.51.100.0/24",
            CorrelationId = "req-1",
            OccurredAtUtc = occurredAt,
            RecordedAtUtc = occurredAt,
            ExpiresAtUtc = expiresAt ?? occurredAt.AddDays(90),
        };

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static DbContextOptions<AppDatabaseContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(connection)
            .Options;
}

/// <summary>A clock that reads whatever the test last set.</summary>
internal sealed class HistoryClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
