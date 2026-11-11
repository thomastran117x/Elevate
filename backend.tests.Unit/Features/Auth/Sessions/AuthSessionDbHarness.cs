using backend.main.features.auth.sessions;
using backend.main.features.auth.token;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Auth.Sessions;

/// <summary>An in-memory SQLite database with the session registry over it.</summary>
internal sealed class AuthSessionDbHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public AppDatabaseContext Db
    {
        get;
    }

    public AuthSessionRepository Repository
    {
        get;
    }

    private AuthSessionDbHarness(SqliteConnection connection, AppDatabaseContext db)
    {
        _connection = connection;
        Db = db;
        Repository = new AuthSessionRepository(db);
    }

    public static async Task<AuthSessionDbHarness> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var db = new AppDatabaseContext(
            new DbContextOptionsBuilder<AppDatabaseContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        return new AuthSessionDbHarness(connection, db);
    }

    public async Task<int> SeedUserAsync(string username, int authVersion = 1, bool disabled = false)
    {
        var user = new User
        {
            Email = $"{username}@example.com",
            Password = "seed-password",
            Usertype = "participant",
            Name = "Seed User",
            Username = username,
            AuthVersion = authVersion,
            IsDisabled = disabled,
        };

        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return user.Id;
    }

    public async Task<AuthSession> SeedSessionAsync(
        int userId,
        string id,
        DateTime lastSeenAt,
        DateTime expiresAt,
        DateTime? revokedAt = null)
    {
        var session = NewSession(userId, id, lastSeenAt, expiresAt);
        session.RevokedAt = revokedAt;
        Db.AuthSessions.Add(session);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return session;
    }

    public Task<AuthSession?> FindAsync(string id) =>
        Db.AuthSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);

    public static AuthSession NewSession(int userId, string id, DateTime lastSeenAt, DateTime expiresAt) => new()
    {
        Id = id,
        UserId = userId,
        Transport = SessionTransport.BrowserCookie,
        CreatedAt = lastSeenAt,
        LastSeenAt = lastSeenAt,
        ExpiresAt = expiresAt,
    };

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
