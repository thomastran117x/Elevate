using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.events;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.registration;
using backend.main.features.events.waitlist;
using backend.main.features.events.waitlist.contracts.responses;
using backend.main.shared.providers.messages;
using backend.main.shared.storage;
using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Events;

public class EventWaitlistEndpointsTests
{
    [Fact]
    public async Task Waitlist_ShouldQueueUsersInJoinOrder_AndResequenceWhenSomeoneLeaves()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-order-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Order Club");
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity: 1);

        // Fill the single seat.
        var occupant = await CreateUserSessionAsync(app, "wl-order-occupant@example.com");
        await RegisterAsync(app, occupant.Session.AccessToken, ev.Id);

        var first = await CreateUserSessionAsync(app, "wl-order-first@example.com");
        var second = await CreateUserSessionAsync(app, "wl-order-second@example.com");

        (await JoinWaitlistAsync(app, first.Session.AccessToken, ev.Id)).Position.Should().Be(1);
        (await JoinWaitlistAsync(app, second.Session.AccessToken, ev.Id)).Position.Should().Be(2);

        (await GetMyWaitlistStatusAsync(app, second.Session.AccessToken, ev.Id)).Position.Should().Be(2);

        // The person ahead leaves — positions below shift up because they are computed.
        var left = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete, $"/api/events/{ev.Id}/waitlist", first.Session.AccessToken));
        left.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await GetMyWaitlistStatusAsync(app, second.Session.AccessToken, ev.Id);
        status.Position.Should().Be(1);
        status.WaitlistCount.Should().Be(1);
    }

    [Fact]
    public async Task Unregistering_ShouldAutoPromoteNextInLine_AndEmailThem()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-promote-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Promote Club");
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity: 1);

        var occupant = await CreateUserSessionAsync(app, "wl-promote-occupant@example.com");
        await RegisterAsync(app, occupant.Session.AccessToken, ev.Id);

        var waiting = await CreateUserSessionAsync(app, "wl-promote-waiting@example.com");
        await JoinWaitlistAsync(app, waiting.Session.AccessToken, ev.Id);

        app.Publisher.Clear();

        var unregistered = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete, $"/api/events/{ev.Id}/register", occupant.Session.AccessToken));
        unregistered.StatusCode.Should().Be(HttpStatusCode.OK);

        // The waitlisted user now holds an active registration.
        var promotedRegistration = await app.QueryDbAsync(db => db.EventRegistrations
            .AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.EventId == ev.Id &&
                r.UserId == waiting.User!.Id &&
                r.Status == RegistrationStatus.Active));
        promotedRegistration.Should().NotBeNull();

        var entry = await app.QueryDbAsync(db => db.EventWaitlistEntries
            .AsNoTracking()
            .SingleAsync(w => w.EventId == ev.Id && w.UserId == waiting.User!.Id));
        entry.Status.Should().Be(EventWaitlistEntryStatus.Promoted);

        // The seat was refilled in the same transaction, so the counter never dipped.
        var stored = await app.QueryDbAsync(db => db.Events.AsNoTracking().SingleAsync(e => e.Id == ev.Id));
        stored.RegistrationCount.Should().Be(1);
        stored.WaitlistCount.Should().Be(0);

        var email = app.Publisher.EmailMessages.Should()
            .ContainSingle(m => m.Type == EmailMessageType.WaitlistPromoted).Subject;
        email.Email.Should().Be(waiting.User!.Email);
        email.EventId.Should().Be(ev.Id);
    }

    [Fact]
    public async Task RaisingCapacity_ShouldPromoteEveryoneWhoFits()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-capacity-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Capacity Club");
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity: 1);

        var occupant = await CreateUserSessionAsync(app, "wl-capacity-occupant@example.com");
        await RegisterAsync(app, occupant.Session.AccessToken, ev.Id);

        var waiters = new List<(AuthenticatedSessionResponse Session, backend.main.features.profile.User? User)>();
        for (var i = 0; i < 3; i++)
        {
            var waiter = await CreateUserSessionAsync(app, $"wl-capacity-waiter{i}@example.com");
            await JoinWaitlistAsync(app, waiter.Session.AccessToken, ev.Id);
            waiters.Add(waiter);
        }

        app.Publisher.Clear();

        // 1 -> 3 seats frees two of the three waiting places.
        var patched = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"/api/events/{ev.Id}/draft",
            organizer.Session.AccessToken,
            JsonContent.Create(new { maxParticipants = 3 })));
        patched.StatusCode.Should().Be(HttpStatusCode.OK);

        var activeCount = await app.QueryDbAsync(db => db.EventRegistrations
            .CountAsync(r => r.EventId == ev.Id && r.Status == RegistrationStatus.Active));
        activeCount.Should().Be(3);

        var stillWaiting = await app.QueryDbAsync(db => db.EventWaitlistEntries
            .CountAsync(w => w.EventId == ev.Id && w.Status == EventWaitlistEntryStatus.Waiting));
        stillWaiting.Should().Be(1);

        app.Publisher.EmailMessages
            .Count(m => m.Type == EmailMessageType.WaitlistPromoted)
            .Should().Be(2);
    }

    [Fact]
    public async Task JoiningWaitlist_ShouldBeRejected_WhenSeatsAreStillAvailable()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-open-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Open Club");
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity: 5);

        var user = await CreateUserSessionAsync(app, "wl-open-user@example.com");
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{ev.Id}/waitlist", user.Session.AccessToken, JsonContent.Create(new { })));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task JoiningWaitlist_ShouldBeRejected_WhenTheEventHasNoWaitlist()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-off-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Off Club");
        var ev = await CreateWaitlistEventAsync(
            app, organizer.Session.AccessToken, club.Id, capacity: 1, waitlistEnabled: false);

        var occupant = await CreateUserSessionAsync(app, "wl-off-occupant@example.com");
        await RegisterAsync(app, occupant.Session.AccessToken, ev.Id);

        var user = await CreateUserSessionAsync(app, "wl-off-user@example.com");
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{ev.Id}/waitlist", user.Session.AccessToken, JsonContent.Create(new { })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task JoiningWaitlist_ShouldRequireAuthentication()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-anon-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Anon Club");
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity: 1);

        var response = await app.Client.PostAsync($"/api/events/{ev.Id}/waitlist", JsonContent.Create(new { }));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OrganizerRoster_ShouldExposePii_ButNonOrganizersAreForbidden()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-roster-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Roster Club");
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity: 1);

        var occupant = await CreateUserSessionAsync(app, "wl-roster-occupant@example.com");
        await RegisterAsync(app, occupant.Session.AccessToken, ev.Id);

        var waiting = await CreateUserSessionAsync(app, "wl-roster-waiting@example.com");
        await JoinWaitlistAsync(app, waiting.Session.AccessToken, ev.Id);

        var roster = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get, $"/api/events/{ev.Id}/waitlist", organizer.Session.AccessToken));
        roster.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = (await app.ReadApiResponseAsync<List<EventWaitlistEntryResponse>>(roster)).Data!;
        entries.Should().ContainSingle();
        entries[0].Position.Should().Be(1);
        entries[0].UserEmail.Should().Be(waiting.User!.Email);

        var forbidden = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get, $"/api/events/{ev.Id}/waitlist", waiting.Session.AccessToken));
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Organizer_ShouldRemoveEntries_AndPromoteManually()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-manage-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Manage Club");
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity: 1);

        var occupant = await CreateUserSessionAsync(app, "wl-manage-occupant@example.com");
        await RegisterAsync(app, occupant.Session.AccessToken, ev.Id);

        var removable = await CreateUserSessionAsync(app, "wl-manage-removable@example.com");
        var entry = await JoinWaitlistAsync(app, removable.Session.AccessToken, ev.Id);

        var removed = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete, $"/api/events/{ev.Id}/waitlist/{entry.Id}", organizer.Session.AccessToken));
        removed.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await app.QueryDbAsync(db => db.EventWaitlistEntries
            .AsNoTracking()
            .SingleAsync(w => w.Id == entry.Id));
        stored.Status.Should().Be(EventWaitlistEntryStatus.Removed);
        stored.RemovedByUserId.Should().Be(organizer.User!.Id);

        // Nobody is left waiting and the event is still full, so there is nothing to promote.
        var promoted = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{ev.Id}/waitlist/promote", organizer.Session.AccessToken,
            JsonContent.Create(new { })));
        promoted.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task MyWaitlists_ShouldListQueuedEventsWithPosition()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-mine-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Mine Club");
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity: 1);

        var occupant = await CreateUserSessionAsync(app, "wl-mine-occupant@example.com");
        await RegisterAsync(app, occupant.Session.AccessToken, ev.Id);

        var waiting = await CreateUserSessionAsync(app, "wl-mine-waiting@example.com");
        await JoinWaitlistAsync(app, waiting.Session.AccessToken, ev.Id);

        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get, "/api/events/me/waitlisted", waiting.Session.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = (await app.ReadApiResponseAsync<List<WaitlistedEventResponse>>(response)).Data!;
        mine.Should().ContainSingle();
        mine[0].Position.Should().Be(1);
        mine[0].Event.Id.Should().Be(ev.Id);
    }

    [Fact]
    public async Task EventResponse_ShouldCarryWaitlistFields()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-fields-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Fields Club");
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity: 1);

        var occupant = await CreateUserSessionAsync(app, "wl-fields-occupant@example.com");
        await RegisterAsync(app, occupant.Session.AccessToken, ev.Id);

        var waiting = await CreateUserSessionAsync(app, "wl-fields-waiting@example.com");
        await JoinWaitlistAsync(app, waiting.Session.AccessToken, ev.Id);

        var response = await app.Client.GetAsync($"/api/events/{ev.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = (await app.ReadApiResponseAsync<EventResponse>(response)).Data!;
        payload.WaitlistEnabled.Should().BeTrue();
        payload.WaitlistCount.Should().Be(1);
    }

    // ---- Concurrency ----

    [Fact]
    public async Task ConcurrentJoins_ShouldProduceOneRowPerUser_WithDistinctPositions()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-conc-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Concurrency Club");
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity: 1);

        var occupant = await CreateUserSessionAsync(app, "wl-conc-occupant@example.com");
        await RegisterAsync(app, occupant.Session.AccessToken, ev.Id);

        var sessions = new List<AuthenticatedSessionResponse>();
        for (var i = 0; i < 6; i++)
        {
            sessions.Add((await CreateUserSessionAsync(app, $"wl-conc-user{i}@example.com")).Session);
        }

        var responses = await Task.WhenAll(sessions.Select(session => app.Client.SendAsync(
            CreateAuthorizedRequest(
                HttpMethod.Post, $"/api/events/{ev.Id}/waitlist", session.AccessToken, JsonContent.Create(new { })))));

        // A losing racer gets a clean 409 from the lock rather than an error.
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().BeGreaterThan(0);
        responses.Should().OnlyContain(r =>
            r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict);

        var entries = await app.QueryDbAsync(db => db.EventWaitlistEntries
            .AsNoTracking()
            .Where(w => w.EventId == ev.Id && w.Status == EventWaitlistEntryStatus.Waiting)
            .ToListAsync());

        entries.Select(e => e.UserId).Should().OnlyHaveUniqueItems();
        entries.Count.Should().Be(responses.Count(r => r.StatusCode == HttpStatusCode.Created));

        var storedCount = await app.QueryDbAsync(db => db.Events
            .AsNoTracking()
            .Where(e => e.Id == ev.Id)
            .Select(e => e.WaitlistCount)
            .SingleAsync());
        storedCount.Should().Be(entries.Count);
    }

    [Fact]
    public async Task SameUserJoiningTwiceConcurrently_ShouldCreateExactlyOneRow()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-dupe-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Dupe Club");
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity: 1);

        var occupant = await CreateUserSessionAsync(app, "wl-dupe-occupant@example.com");
        await RegisterAsync(app, occupant.Session.AccessToken, ev.Id);

        var user = await CreateUserSessionAsync(app, "wl-dupe-user@example.com");

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => app.Client.SendAsync(
            CreateAuthorizedRequest(
                HttpMethod.Post, $"/api/events/{ev.Id}/waitlist", user.Session.AccessToken,
                JsonContent.Create(new { })))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        var rows = await app.QueryDbAsync(db => db.EventWaitlistEntries
            .CountAsync(w => w.EventId == ev.Id && w.UserId == user.User!.Id));
        rows.Should().Be(1);
    }

    [Fact]
    public async Task UnregisterRacingRegistrations_ShouldNeverExceedCapacity()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-race-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Race Club");
        const int capacity = 4;
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity);

        // Fill every seat.
        var occupants = new List<AuthenticatedSessionResponse>();
        for (var i = 0; i < capacity; i++)
        {
            var occupant = await CreateUserSessionAsync(app, $"wl-race-occupant{i}@example.com");
            await RegisterAsync(app, occupant.Session.AccessToken, ev.Id);
            occupants.Add(occupant.Session);
        }

        // Queue two people.
        for (var i = 0; i < 2; i++)
        {
            var waiter = await CreateUserSessionAsync(app, $"wl-race-waiter{i}@example.com");
            await JoinWaitlistAsync(app, waiter.Session.AccessToken, ev.Id);
        }

        // Outsiders trying to grab the seat the instant it frees.
        var outsiders = new List<AuthenticatedSessionResponse>();
        for (var i = 0; i < 4; i++)
        {
            outsiders.Add((await CreateUserSessionAsync(app, $"wl-race-outsider{i}@example.com")).Session);
        }

        var work = new List<Task<HttpResponseMessage>>
        {
            app.Client.SendAsync(CreateAuthorizedRequest(
                HttpMethod.Delete, $"/api/events/{ev.Id}/register", occupants[0].AccessToken))
        };
        work.AddRange(outsiders.Select(session => app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{ev.Id}/register", session.AccessToken, JsonContent.Create(new { })))));

        await Task.WhenAll(work);

        var activeCount = await app.QueryDbAsync(db => db.EventRegistrations
            .CountAsync(r => r.EventId == ev.Id && r.Status == RegistrationStatus.Active));

        // The core invariant: capacity is never exceeded, and the denormalized counter agrees
        // with the source of truth no matter who won the race.
        activeCount.Should().BeLessThanOrEqualTo(capacity);

        var stored = await app.QueryDbAsync(db => db.Events.AsNoTracking().SingleAsync(e => e.Id == ev.Id));
        stored.RegistrationCount.Should().Be(activeCount);
    }

    [Fact]
    public async Task ConcurrentUnregisters_ShouldPromoteOnlyAsManyAsSeatsFreed()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "wl-double-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Waitlist Double Club");
        var ev = await CreateWaitlistEventAsync(app, organizer.Session.AccessToken, club.Id, capacity: 2);

        var firstOccupant = await CreateUserSessionAsync(app, "wl-double-occ1@example.com");
        var secondOccupant = await CreateUserSessionAsync(app, "wl-double-occ2@example.com");
        await RegisterAsync(app, firstOccupant.Session.AccessToken, ev.Id);
        await RegisterAsync(app, secondOccupant.Session.AccessToken, ev.Id);

        // Only ONE person waiting, but TWO seats are about to free.
        var waiting = await CreateUserSessionAsync(app, "wl-double-waiting@example.com");
        await JoinWaitlistAsync(app, waiting.Session.AccessToken, ev.Id);

        await Task.WhenAll(
            app.Client.SendAsync(CreateAuthorizedRequest(
                HttpMethod.Delete, $"/api/events/{ev.Id}/register", firstOccupant.Session.AccessToken)),
            app.Client.SendAsync(CreateAuthorizedRequest(
                HttpMethod.Delete, $"/api/events/{ev.Id}/register", secondOccupant.Session.AccessToken)));

        var promotedRows = await app.QueryDbAsync(db => db.EventWaitlistEntries
            .CountAsync(w => w.EventId == ev.Id && w.Status == EventWaitlistEntryStatus.Promoted));
        promotedRows.Should().Be(1, "there was only one person to promote");

        var activeForWaiting = await app.QueryDbAsync(db => db.EventRegistrations
            .CountAsync(r =>
                r.EventId == ev.Id &&
                r.UserId == waiting.User!.Id &&
                r.Status == RegistrationStatus.Active));
        activeForWaiting.Should().Be(1, "the unique index forbids a duplicate registration");
    }

    // ---- Helpers ----

    private static async Task<(AuthenticatedSessionResponse Session, backend.main.features.profile.User? User)>
        CreateUserSessionAsync(AuthApiTestApp app, string email, string role = "Participant")
    {
        var session = await app.SignUpAndVerifyByTokenAsync(
            email, role: role, transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync(email);
        return (session, user);
    }

    private static async Task<ClubApiModel> CreateClubAsync(AuthApiTestApp app, string accessToken, string name)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs",
            accessToken,
            JsonContent.Create(new
            {
                Name = name,
                Description = "Waitlist testing group",
                Clubtype = "social",
                ClubImageUrl = app.BlobStorage.CreateOwnedBlobUrl("clubs", "club.png"),
                Email = $"{name.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}@example.com"
            })));

        if (response.StatusCode != HttpStatusCode.Created)
            throw new Xunit.Sdk.XunitException(await app.DescribeFailureAsync(response));

        return (await app.ReadApiResponseAsync<ClubApiModel>(response)).Data!;
    }

    /// <summary>Creates and publishes a free, capacity-limited event with a waitlist.</summary>
    private static async Task<EventResponse> CreateWaitlistEventAsync(
        AuthApiTestApp app,
        string accessToken,
        int clubId,
        int capacity,
        bool waitlistEnabled = true)
    {
        var presigned = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/images/presigned-url",
            accessToken,
            JsonContent.Create(new { clubId, fileName = "poster.png", contentType = "image/png" })));
        presigned.StatusCode.Should().Be(HttpStatusCode.OK);
        var image = (await app.ReadApiResponseAsync<PresignedUploadResponse>(presigned)).Data!;

        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/clubs/{clubId}/drafts",
            accessToken,
            JsonContent.Create(new
            {
                name = "Waitlisted Event",
                description = "A published event used for waitlist integration coverage.",
                location = "Student Center",
                imageUrls = new[] { image.PublicUrl },
                isPrivate = false,
                maxParticipants = capacity,
                registerCost = 0,
                waitlistEnabled,
                startTime = DateTime.UtcNow.AddDays(6),
                endTime = DateTime.UtcNow.AddDays(6).AddHours(2),
                category = EventCategory.Other,
                venueName = "Room A",
                city = "Toronto",
                tags = new[] { "testing" }
            })));

        if (created.StatusCode != HttpStatusCode.Created)
            throw new Xunit.Sdk.XunitException(await app.DescribeFailureAsync(created));

        var draft = (await app.ReadApiResponseAsync<ManagedEventResponse>(created)).Data!;

        var published = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{draft.Id}/publish", accessToken, JsonContent.Create(new { })));

        if (published.StatusCode != HttpStatusCode.OK)
            throw new Xunit.Sdk.XunitException(await app.DescribeFailureAsync(published));

        var managed = (await app.ReadApiResponseAsync<ManagedEventResponse>(published)).Data!;
        return new EventResponse
        {
            Id = managed.Id,
            Name = managed.Name ?? string.Empty,
            MaxParticipants = managed.MaxParticipants ?? 0,
            WaitlistEnabled = managed.WaitlistEnabled,
            WaitlistCount = managed.WaitlistCount,
            ClubId = managed.ClubId
        };
    }

    private static async Task RegisterAsync(AuthApiTestApp app, string accessToken, int eventId)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{eventId}/register", accessToken, JsonContent.Create(new { })));

        if (response.StatusCode != HttpStatusCode.Created)
            throw new Xunit.Sdk.XunitException(await app.DescribeFailureAsync(response));
    }

    private static async Task<EventWaitlistEntryResponse> JoinWaitlistAsync(
        AuthApiTestApp app, string accessToken, int eventId)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{eventId}/waitlist", accessToken, JsonContent.Create(new { })));

        if (response.StatusCode != HttpStatusCode.Created)
            throw new Xunit.Sdk.XunitException(await app.DescribeFailureAsync(response));

        return (await app.ReadApiResponseAsync<EventWaitlistEntryResponse>(response)).Data!;
    }

    private static async Task<MyWaitlistStatusResponse> GetMyWaitlistStatusAsync(
        AuthApiTestApp app, string accessToken, int eventId)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get, $"/api/events/{eventId}/waitlist/me", accessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await app.ReadApiResponseAsync<MyWaitlistStatusResponse>(response)).Data!;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string accessToken,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = content;
        return request;
    }

    private sealed class ClubApiModel
    {
        public int Id
        {
            get; set;
        }
        public string Name { get; set; } = string.Empty;
    }
}
