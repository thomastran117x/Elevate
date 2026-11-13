using backend.main.application.security;
using backend.main.features.clubs;
using backend.main.features.clubs.moderation;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;
using backend.tests.Unit.Support;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Clubs.Moderation;

public class ClubModerationLogTests
{
    private const int ClubId = 7;
    private const int OtherClubId = 8;
    private const int OwnerId = 11;
    private const int MemberId = 12;

    [Fact]
    public async Task Stage_ShouldCommitWithTheNextSave()
    {
        await using var harness = await Harness.CreateAsync();

        harness.Log.Stage(Entry(ClubId, contentId: 1, DateTime.UtcNow));
        harness.Db.ClubModerationActions.Local.Should().ContainSingle();
        (await harness.Db.ClubModerationActions.CountAsync()).Should().Be(0, "staging does not save");

        await harness.Db.SaveChangesAsync();

        (await harness.Db.ClubModerationActions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetPageAsync_ShouldReturnTheClubsEntriesNewestFirst_AndPage()
    {
        await using var harness = await Harness.CreateAsync();
        var baseTime = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        harness.Log.Stage(Entry(ClubId, contentId: 1, baseTime));
        harness.Log.Stage(Entry(ClubId, contentId: 2, baseTime.AddHours(2)));
        harness.Log.Stage(Entry(ClubId, contentId: 3, baseTime.AddHours(1)));
        harness.Log.Stage(Entry(OtherClubId, contentId: 4, baseTime.AddHours(3)));
        await harness.Db.SaveChangesAsync();

        var (firstPage, total) = await harness.Log.GetPageAsync(ClubId, OwnerId, AuthRoles.Organizer, page: 1, pageSize: 2);
        var (secondPage, _) = await harness.Log.GetPageAsync(ClubId, OwnerId, AuthRoles.Organizer, page: 2, pageSize: 2);

        total.Should().Be(3);
        firstPage.Select(entry => entry.ContentId).Should().Equal(2, 3);
        secondPage.Select(entry => entry.ContentId).Should().Equal(1);
    }

    [Fact]
    public async Task GetPageAsync_ShouldRefuseCallersWhoCannotManageTheClub()
    {
        await using var harness = await Harness.CreateAsync();

        var act = () => harness.Log.GetPageAsync(ClubId, MemberId, AuthRoles.Participant, 1, 20);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage(ClubModerationLog.ReadForbiddenMessage);
    }

    [Fact]
    public async Task GetPageAsync_ShouldAllowAdmins()
    {
        await using var harness = await Harness.CreateAsync();

        var (items, total) = await harness.Log.GetPageAsync(ClubId, MemberId, AuthRoles.Admin, 1, 20);

        items.Should().BeEmpty();
        total.Should().Be(0);
    }

    private static ClubModerationAction Entry(int clubId, int contentId, DateTime createdAt) => new()
    {
        ClubId = clubId,
        ActorUserId = OwnerId,
        ActorClubRole = ClubModerationRoles.Owner,
        Action = ClubModerationActionTypes.Remove,
        ContentType = ClubModerationContentTypes.Comment,
        ContentId = contentId,
        AuthorUserId = MemberId,
        Reason = "Spam",
        CreatedAt = createdAt,
    };

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public ClubModerationLog Log { get; }

        private Harness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;
            Log = new ClubModerationLog(db, TestResourceAuthorizers.Over(db));
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

            db.Users.AddRange(
                new User { Id = OwnerId, Email = "owner@test.local", Usertype = "Organizer" },
                new User { Id = MemberId, Email = "member@test.local", Usertype = "Participant" });
            db.Clubs.AddRange(NewClub(ClubId, "Moderated Club"), NewClub(OtherClubId, "Another Club"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            return new Harness(connection, db);
        }

        private static Club NewClub(int id, string name) => new()
        {
            Id = id,
            UserId = OwnerId,
            Name = name,
            Description = "A club used for moderation log tests.",
            Clubtype = ClubType.Gaming,
            ClubImage = "https://cdn.test/clubs/moderation.png"
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
