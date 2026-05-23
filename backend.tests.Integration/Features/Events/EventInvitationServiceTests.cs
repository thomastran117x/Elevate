using System.Security.Cryptography;
using System.Text;

using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.events;
using backend.main.features.events.images;
using backend.main.features.events.invitations;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.events.versions;
using backend.main.features.payment;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;
using backend.main.shared.providers;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace backend.tests.Events;

public sealed class EventInvitationServiceTests
{
    [Fact]
    public async Task GetVisibleEvent_AllowsAcceptedInvitation()
    {
        await using var harness = await InvitationHarness.CreateAsync();
        harness.Db.EventInvitations.Add(new EventInvitation
        {
            EventId = harness.EventId,
            RecipientUserId = harness.UserId,
            RecipientEmail = harness.UserEmail,
            RecipientEmailNormalized = NormalizeEmail(harness.UserEmail),
            SourceType = EventInvitationSource.DirectEmail,
            LifecycleStatus = EventInvitationLifecycleStatus.Accepted,
            DeliveryStatus = EventInvitationDeliveryStatus.Sent,
            AcceptedAtUtc = DateTime.UtcNow
        });
        await harness.Db.SaveChangesAsync();

        var visible = await harness.EventsService.GetVisibleEvent(harness.EventId, harness.UserId, "Participant");

        visible.Id.Should().Be(harness.EventId);
    }

    [Fact]
    public async Task GetVisibleEvent_HidesPendingInvitation()
    {
        await using var harness = await InvitationHarness.CreateAsync();
        harness.Db.EventInvitations.Add(new EventInvitation
        {
            EventId = harness.EventId,
            RecipientUserId = harness.UserId,
            RecipientEmail = harness.UserEmail,
            RecipientEmailNormalized = NormalizeEmail(harness.UserEmail),
            SourceType = EventInvitationSource.DirectEmail,
            LifecycleStatus = EventInvitationLifecycleStatus.Pending,
            DeliveryStatus = EventInvitationDeliveryStatus.Sent,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await harness.Db.SaveChangesAsync();

        var action = () => harness.EventsService.GetVisibleEvent(harness.EventId, harness.UserId, "Participant");

        await action.Should().ThrowAsync<ResourceNotFoundException>();
    }

    [Fact]
    public async Task AcceptInvitationById_BindsPendingEmailInviteToMatchingUser()
    {
        await using var harness = await InvitationHarness.CreateAsync();
        var invitation = new EventInvitation
        {
            EventId = harness.EventId,
            RecipientEmail = harness.UserEmail,
            RecipientEmailNormalized = NormalizeEmail(harness.UserEmail),
            SourceType = EventInvitationSource.DirectEmail,
            LifecycleStatus = EventInvitationLifecycleStatus.Pending,
            DeliveryStatus = EventInvitationDeliveryStatus.Sent,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };

        harness.Db.EventInvitations.Add(invitation);
        await harness.Db.SaveChangesAsync();

        var result = await harness.InvitationService.AcceptInvitationByIdAsync(invitation.Id, harness.UserId, harness.UserEmail);

        result.Invitation.EffectiveStatus.Should().Be("Accepted");
        invitation.RecipientUserId.Should().Be(harness.UserId);
        invitation.AcceptedByUserId.Should().Be(harness.UserId);
    }

    [Fact]
    public async Task ResolveInvitation_ReturnsAcceptAvailable_ForMatchingRecipient()
    {
        await using var harness = await InvitationHarness.CreateAsync();
        const string rawToken = "invite-token";
        harness.Db.EventInvitations.Add(new EventInvitation
        {
            EventId = harness.EventId,
            RecipientEmail = harness.UserEmail,
            RecipientEmailNormalized = NormalizeEmail(harness.UserEmail),
            SourceType = EventInvitationSource.DirectEmail,
            LifecycleStatus = EventInvitationLifecycleStatus.Pending,
            DeliveryStatus = EventInvitationDeliveryStatus.Sent,
            ClaimTokenHash = ComputeTokenHash(rawToken),
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await harness.Db.SaveChangesAsync();

        var resolved = await harness.InvitationService.ResolveInvitationAsync(rawToken, harness.UserId, harness.UserEmail);

        resolved.State.Should().Be(EventInvitationResolveState.AcceptAvailable.ToString());
        resolved.Event.Should().NotBeNull();
        resolved.Event!.Id.Should().Be(harness.EventId);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string ComputeTokenHash(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }

    private sealed class InvitationHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public EventInvitationService InvitationService { get; }
        public EventsService EventsService { get; }
        public int EventId { get; }
        public int UserId { get; }
        public string UserEmail { get; }

        private InvitationHarness(
            SqliteConnection connection,
            AppDatabaseContext db,
            EventInvitationService invitationService,
            EventsService eventsService,
            int eventId,
            int userId,
            string userEmail)
        {
            _connection = connection;
            Db = db;
            InvitationService = invitationService;
            EventsService = eventsService;
            EventId = eventId;
            UserId = userId;
            UserEmail = userEmail;
        }

        public static async Task<InvitationHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();

            var user = new User
            {
                Email = "guest@example.com",
                Usertype = "Participant"
            };
            db.Users.Add(user);

            var organizer = new User
            {
                Email = "organizer@example.com",
                Usertype = "Organizer"
            };
            db.Users.Add(organizer);
            await db.SaveChangesAsync();

            var club = new Club
            {
                Name = "Board Game Club",
                Description = "Board games and puzzles.",
                Clubtype = ClubType.Gaming,
                ClubImage = "/club.png",
                UserId = organizer.Id
            };
            db.Clubs.Add(club);
            await db.SaveChangesAsync();

            var ev = new backend.main.features.events.Events
            {
                Name = "Invite Only Night",
                Description = "A private strategy night.",
                Location = "Student Center",
                StartTime = DateTime.UtcNow.AddDays(2),
                EndTime = DateTime.UtcNow.AddDays(2).AddHours(3),
                ClubId = club.Id,
                isPrivate = true,
                Category = EventCategory.Gaming
            };
            db.Events.Add(ev);
            await db.SaveChangesAsync();

            var clubService = new Mock<IClubService>();
            clubService.Setup(service => service.HasClubStaffAccessAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>()))
                .ReturnsAsync(false);

            var invitationService = new EventInvitationService(
                db,
                clubService.Object,
                Mock.Of<IUserRepository>(),
                Mock.Of<IPublisher>(),
                TimeProvider.System);

            var eventsService = new EventsService(
                db,
                new EventsRepository(db),
                new EventImageRepository(db),
                clubService.Object,
                Mock.Of<backend.main.shared.storage.IAzureBlobService>(),
                new NoOpCacheService(),
                Mock.Of<backend.main.features.events.analytics.IEventAnalyticsRepository>(),
                Mock.Of<IEventSearchService>(),
                Mock.Of<IEventSearchOutboxWriter>(),
                new EventRegistrationRepository(db),
                invitationService,
                Options.Create(new EventVersioningOptions()),
                TimeProvider.System);

            return new InvitationHarness(connection, db, invitationService, eventsService, ev.Id, user.Id, user.Email);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
