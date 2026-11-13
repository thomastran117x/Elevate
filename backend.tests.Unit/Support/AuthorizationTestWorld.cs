using backend.main.application.security;
using backend.main.application.security.authorization;
using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.staff;
using backend.main.features.events;
using backend.main.features.events.access;
using backend.main.features.events.invitations;
using backend.main.features.events.registration;
using backend.main.features.events.series;
using backend.main.features.media;
using backend.main.features.payment;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace backend.tests.Unit.Support;

/// <summary>The callers the authorization matrix is written for.</summary>
public enum Caller
{
    Anonymous,
    Unrelated,
    Member,
    Volunteer,
    Manager,
    Owner,
    Admin,

    /// <summary>Manager of a different club: proves one club's staff roles do not leak into another.</summary>
    OtherClubManager,
    Registrant,
    Invitee,
    Payer,
}

/// <summary>
/// A seeded SQLite world with one caller per <see cref="Caller"/> and the real resource
/// authorizer wired over it. Two clubs (one public, one private) share the same owner, staff and
/// members; a third club has its own manager for cross-club isolation.
/// </summary>
internal sealed class AuthorizationTestWorld : IAsyncDisposable
{
    public const int PublicClubId = 10;
    public const int PrivateClubId = 11;
    public const int OtherClubId = 12;
    public const int MissingId = 9_999;

    public const int PublicEventId = 100;
    public const int PrivateEventId = 101;
    public const int DraftEventId = 102;
    public const int PrivateClubEventId = 103;

    public const int SeriesId = 200;
    public const int OwnPaymentId = 300;
    public static readonly Guid ClubAssetId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid LooseAssetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Dictionary<Caller, (int Id, string Role)> Users = new()
    {
        [Caller.Owner] = (1, AuthRoles.Organizer),
        [Caller.Manager] = (2, AuthRoles.Participant),
        [Caller.Volunteer] = (3, AuthRoles.Volunteer),
        [Caller.Member] = (4, AuthRoles.Participant),
        [Caller.Unrelated] = (5, AuthRoles.Organizer),
        [Caller.Admin] = (6, AuthRoles.Admin),
        [Caller.OtherClubManager] = (7, AuthRoles.Organizer),
        [Caller.Registrant] = (20, AuthRoles.Participant),
        [Caller.Invitee] = (21, AuthRoles.Participant),
        [Caller.Payer] = (22, AuthRoles.Participant),
    };

    private const int OtherClubOwnerId = 8;

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    private AuthorizationTestWorld(SqliteConnection connection, ServiceProvider provider)
    {
        _connection = connection;
        _provider = provider;
        _scope = provider.CreateScope();
    }

    public AppDatabaseContext Db => _scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();

    public IResourceAuthorizer Authorizer => _scope.ServiceProvider.GetRequiredService<IResourceAuthorizer>();

    public IAuthorizationService AuthorizationService =>
        _scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

    public static int UserId(Caller caller) => Users[caller].Id;

    public static ResourceActor Actor(Caller caller) =>
        caller == Caller.Anonymous ? ResourceActor.Anonymous : ResourceActor.User(Users[caller].Id, Users[caller].Role);

    public static async Task<AuthorizationTestWorld> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddDbContext<AppDatabaseContext>(options => options.UseSqlite(connection));
        services.AddResourceAuthorization();

        // The visibility policy is the real one. Its collaborators are reduced to what it reads:
        // club staff access through the authorizer, registrations from the database, and accepted
        // invitations for the invitee.
        services.AddScoped<IClubService>(sp =>
        {
            var authorizer = sp.GetRequiredService<IResourceAuthorizer>();
            var clubService = new Mock<IClubService>();
            clubService
                .Setup(service => service.HasClubStaffAccessAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>()))
                .Returns((int clubId, int userId, string? role) => authorizer.IsAllowedAsync(
                    ResourceActor.User(userId, role), Permissions.ClubStaffAccess, new ClubResource(clubId)));
            return clubService.Object;
        });
        services.AddScoped<IEventRegistrationRepository>(sp =>
            new EventRegistrationRepository(sp.GetRequiredService<AppDatabaseContext>()));
        services.AddScoped<IEventInvitationService>(_ =>
        {
            var invitations = new Mock<IEventInvitationService>();
            invitations
                .Setup(service => service.HasAcceptedInvitationAccessAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((int eventId, int userId) =>
                    eventId == PrivateEventId && userId == Users[Caller.Invitee].Id);
            return invitations.Object;
        });
        services.AddScoped<IEventAccessChecker, EventAccessChecker>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var world = new AuthorizationTestWorld(connection, provider);
        await world.SeedAsync();
        return world;
    }

    private async Task SeedAsync()
    {
        var db = Db;
        await db.Database.EnsureCreatedAsync();

        foreach (var (caller, user) in Users)
            db.Users.Add(new User { Id = user.Id, Email = $"{caller}@test.local".ToLowerInvariant(), Usertype = user.Role });
        db.Users.Add(new User { Id = OtherClubOwnerId, Email = "other-owner@test.local", Usertype = AuthRoles.Organizer });

        db.Clubs.AddRange(
            NewClub(PublicClubId, Users[Caller.Owner].Id, isPrivate: false),
            NewClub(PrivateClubId, Users[Caller.Owner].Id, isPrivate: true),
            NewClub(OtherClubId, OtherClubOwnerId, isPrivate: true));

        foreach (var clubId in new[] { PublicClubId, PrivateClubId })
        {
            db.ClubStaff.AddRange(
                NewStaff(clubId, Users[Caller.Manager].Id, ClubStaffRole.Manager),
                NewStaff(clubId, Users[Caller.Volunteer].Id, ClubStaffRole.Volunteer));
            db.FollowClubs.Add(new FollowClub { ClubId = clubId, UserId = Users[Caller.Member].Id });
        }

        db.ClubStaff.Add(NewStaff(OtherClubId, Users[Caller.OtherClubManager].Id, ClubStaffRole.Manager));

        db.Events.AddRange(
            NewEvent(PublicEventId, PublicClubId, isPrivate: false, EventLifecycleState.Published),
            NewEvent(PrivateEventId, PublicClubId, isPrivate: true, EventLifecycleState.Published),
            NewEvent(DraftEventId, PublicClubId, isPrivate: false, EventLifecycleState.Draft),
            NewEvent(PrivateClubEventId, PrivateClubId, isPrivate: false, EventLifecycleState.Published));

        db.EventRegistrations.Add(new EventRegistration
        {
            EventId = PrivateEventId,
            UserId = Users[Caller.Registrant].Id,
            Status = RegistrationStatus.Active,
        });

        db.Payments.AddRange(
            new Payment { Id = OwnPaymentId, EventId = PrivateEventId, UserId = Users[Caller.Payer].Id, Amount = 500, Status = PaymentStatus.Pending });

        db.EventSeries.Add(new EventSeries
        {
            Id = SeriesId,
            ClubId = PublicClubId,
            CreatedByUserId = Users[Caller.Owner].Id,
            FirstOccurrenceLocalStart = new DateTime(2027, 1, 1, 18, 0, 0),
        });

        db.MediaAssets.AddRange(
            new MediaAsset
            {
                PublicId = ClubAssetId,
                OwnerUserId = Users[Caller.Volunteer].Id,
                ClubId = PublicClubId,
                DeclaredContentType = "image/png",
            },
            new MediaAsset
            {
                PublicId = LooseAssetId,
                OwnerUserId = Users[Caller.Member].Id,
                DeclaredContentType = "image/png",
            });

        await db.SaveChangesAsync();
    }

    private static Club NewClub(int id, int ownerId, bool isPrivate) => new()
    {
        Id = id,
        Name = $"Club {id}",
        Description = "Authorization matrix club.",
        Clubtype = ClubType.Social,
        ClubImage = $"https://cdn.test/clubs/{id}.png",
        UserId = ownerId,
        isPrivate = isPrivate,
    };

    private static ClubStaff NewStaff(int clubId, int userId, ClubStaffRole role) => new()
    {
        ClubId = clubId,
        UserId = userId,
        Role = role,
        GrantedByUserId = clubId == OtherClubId ? OtherClubOwnerId : Users[Caller.Owner].Id,
    };

    private static backend.main.features.events.Events NewEvent(int id, int clubId, bool isPrivate, EventLifecycleState state) => new()
    {
        Id = id,
        ClubId = clubId,
        Name = $"Event {id}",
        Description = "Authorization matrix event.",
        Location = "Hall",
        isPrivate = isPrivate,
        LifecycleState = state,
        StartTime = DateTime.UtcNow.AddDays(3),
        EndTime = DateTime.UtcNow.AddDays(3).AddHours(2),
        Category = EventCategory.Other,
    };

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
