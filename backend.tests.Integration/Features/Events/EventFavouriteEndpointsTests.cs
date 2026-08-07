using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.events;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.favourites.contracts.responses;
using backend.main.shared.storage;
using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Events;

public class EventFavouriteEndpointsTests
{
    [Fact]
    public async Task Favouriting_ShouldStoreOneRow_AndSurfaceInTheIdSet()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "fav-store-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Favourite Store Club");
        var ev = await CreateEventAsync(app, organizer.Session.AccessToken, club.Id);

        var user = await CreateUserSessionAsync(app, "fav-store-user@example.com");

        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{ev.Id}/favourite", user.Session.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var stored = await app.QueryDbAsync(db => db.EventFavourites
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.EventId == ev.Id && f.UserId == user.User!.Id));
        stored.Should().NotBeNull();

        var ids = await GetMyFavouriteIdsAsync(app, user.Session.AccessToken);
        ids.Should().Equal(ev.Id);
    }

    [Fact]
    public async Task Favouriting_ShouldNotChangeRegistrationState()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "fav-noseat-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Favourite No Seat Club");
        var ev = await CreateEventAsync(app, organizer.Session.AccessToken, club.Id);

        var user = await CreateUserSessionAsync(app, "fav-noseat-user@example.com");
        await FavouriteAsync(app, user.Session.AccessToken, ev.Id);

        // The whole point of a star: it is not a commitment, so no seat is consumed.
        var registrations = await app.QueryDbAsync(db => db.EventRegistrations
            .CountAsync(r => r.EventId == ev.Id));
        registrations.Should().Be(0);

        var stored = await app.QueryDbAsync(db => db.Events.AsNoTracking().SingleAsync(e => e.Id == ev.Id));
        stored.RegistrationCount.Should().Be(0);
    }

    [Fact]
    public async Task GetMyStatus_ShouldTrackTheStarForTheCallingUser()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "fav-status-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Favourite Status Club");
        var ev = await CreateEventAsync(app, organizer.Session.AccessToken, club.Id);

        var user = await CreateUserSessionAsync(app, "fav-status-user@example.com");
        var bystander = await CreateUserSessionAsync(app, "fav-status-bystander@example.com");

        var before = await GetMyFavouriteStatusAsync(app, user.Session.AccessToken, ev.Id);
        before.EventId.Should().Be(ev.Id);
        before.IsFavourited.Should().BeFalse();

        await FavouriteAsync(app, user.Session.AccessToken, ev.Id);

        (await GetMyFavouriteStatusAsync(app, user.Session.AccessToken, ev.Id))
            .IsFavourited.Should().BeTrue();

        // The status is per-caller, not per-event.
        (await GetMyFavouriteStatusAsync(app, bystander.Session.AccessToken, ev.Id))
            .IsFavourited.Should().BeFalse();
    }

    [Fact]
    public async Task FavouritingTwice_ShouldSucceed_AndLeaveOneRow()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "fav-dupe-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Favourite Dupe Club");
        var ev = await CreateEventAsync(app, organizer.Session.AccessToken, club.Id);

        var user = await CreateUserSessionAsync(app, "fav-dupe-user@example.com");

        await FavouriteAsync(app, user.Session.AccessToken, ev.Id);
        // A double-tap on the star must not 409 the way a duplicate registration does.
        await FavouriteAsync(app, user.Session.AccessToken, ev.Id);

        var rows = await app.QueryDbAsync(db => db.EventFavourites
            .CountAsync(f => f.EventId == ev.Id && f.UserId == user.User!.Id));
        rows.Should().Be(1);
    }

    [Fact]
    public async Task SameUserFavouritingConcurrently_ShouldCreateExactlyOneRow()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "fav-race-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Favourite Race Club");
        var ev = await CreateEventAsync(app, organizer.Session.AccessToken, club.Id);

        var user = await CreateUserSessionAsync(app, "fav-race-user@example.com");

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => app.Client.SendAsync(
            CreateAuthorizedRequest(
                HttpMethod.Post, $"/api/events/{ev.Id}/favourite", user.Session.AccessToken))));

        // There is no lock here — the unique index plus the read-back is what keeps it correct.
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);

        var rows = await app.QueryDbAsync(db => db.EventFavourites
            .CountAsync(f => f.EventId == ev.Id && f.UserId == user.User!.Id));
        rows.Should().Be(1);
    }

    [Fact]
    public async Task Unfavouriting_ShouldRemoveTheRow_AndBeIdempotent()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "fav-remove-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Favourite Remove Club");
        var ev = await CreateEventAsync(app, organizer.Session.AccessToken, club.Id);

        var user = await CreateUserSessionAsync(app, "fav-remove-user@example.com");
        await FavouriteAsync(app, user.Session.AccessToken, ev.Id);

        var removed = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete, $"/api/events/{ev.Id}/favourite", user.Session.AccessToken));
        removed.StatusCode.Should().Be(HttpStatusCode.OK);

        // Removing again must not 404 — the client may retry, and the end state is the same.
        var removedAgain = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete, $"/api/events/{ev.Id}/favourite", user.Session.AccessToken));
        removedAgain.StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await app.QueryDbAsync(db => db.EventFavourites.CountAsync(f => f.EventId == ev.Id));
        rows.Should().Be(0);
    }

    [Fact]
    public async Task RefavouritingAfterRemoval_ShouldSucceed()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "fav-backtrack-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Favourite Backtrack Club");
        var ev = await CreateEventAsync(app, organizer.Session.AccessToken, club.Id);

        var user = await CreateUserSessionAsync(app, "fav-backtrack-user@example.com");

        // The backtrack path the pinned page depends on: the row stays rendered after an
        // unstar, so the very next click re-stars it and must not hit a stale unique row.
        await FavouriteAsync(app, user.Session.AccessToken, ev.Id);
        var removed = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete, $"/api/events/{ev.Id}/favourite", user.Session.AccessToken));
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        await FavouriteAsync(app, user.Session.AccessToken, ev.Id);

        (await GetMyFavouriteIdsAsync(app, user.Session.AccessToken)).Should().Equal(ev.Id);
    }

    [Fact]
    public async Task GetMyPinned_ShouldUnionRegistrationsAndFavourites_RegisteredFirst()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "fav-pinned-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Favourite Pinned Club");

        // The registered event starts later than the starred one, so a pure start-time sort
        // would put it second.
        var registeredEvent = await CreateEventAsync(app, organizer.Session.AccessToken, club.Id, startsInDays: 9);
        var starredEvent = await CreateEventAsync(app, organizer.Session.AccessToken, club.Id, startsInDays: 4);

        var user = await CreateUserSessionAsync(app, "fav-pinned-user@example.com");
        await RegisterAsync(app, user.Session.AccessToken, registeredEvent.Id);
        await FavouriteAsync(app, user.Session.AccessToken, starredEvent.Id);

        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get, "/api/events/me/pinned", user.Session.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var pinned = (await app.ReadApiResponseAsync<List<PinnedEventResponse>>(response)).Data!;
        pinned.Select(p => p.Event.Id).Should().Equal(registeredEvent.Id, starredEvent.Id);

        pinned[0].IsRegistered.Should().BeTrue();
        pinned[0].IsFavourited.Should().BeFalse("a joined event appears even when it was never starred");
        pinned[1].IsRegistered.Should().BeFalse();
        pinned[1].IsFavourited.Should().BeTrue();
    }

    [Fact]
    public async Task GetMyPinned_ShouldKeepRowsAfterUnfavouriting_OnlyUntilTheNextLoad()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "fav-reload-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Favourite Reload Club");
        var ev = await CreateEventAsync(app, organizer.Session.AccessToken, club.Id);

        var user = await CreateUserSessionAsync(app, "fav-reload-user@example.com");
        await FavouriteAsync(app, user.Session.AccessToken, ev.Id);

        var before = await GetMyPinnedAsync(app, user.Session.AccessToken);
        before.Should().ContainSingle();

        var removed = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete, $"/api/events/{ev.Id}/favourite", user.Session.AccessToken));
        removed.StatusCode.Should().Be(HttpStatusCode.OK);

        // The client keeps the row rendered so it can be re-starred; the server is what drops
        // it, and only on the next load.
        var after = await GetMyPinnedAsync(app, user.Session.AccessToken);
        after.Should().BeEmpty();
    }

    [Fact]
    public async Task Favourite_ShouldRequireAuthentication()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "fav-anon-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Favourite Anon Club");
        var ev = await CreateEventAsync(app, organizer.Session.AccessToken, club.Id);

        var response = await app.Client.PostAsync($"/api/events/{ev.Id}/favourite", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Favourite_ShouldNotLeakAcrossUsers()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserSessionAsync(app, "fav-isolate-organizer@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.Session.AccessToken, "Favourite Isolate Club");
        var ev = await CreateEventAsync(app, organizer.Session.AccessToken, club.Id);

        var owner = await CreateUserSessionAsync(app, "fav-isolate-owner@example.com");
        var bystander = await CreateUserSessionAsync(app, "fav-isolate-bystander@example.com");

        await FavouriteAsync(app, owner.Session.AccessToken, ev.Id);

        (await GetMyFavouriteIdsAsync(app, bystander.Session.AccessToken)).Should().BeEmpty();
        (await GetMyPinnedAsync(app, bystander.Session.AccessToken)).Should().BeEmpty();
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
                Description = "Favourite testing group",
                Clubtype = "social",
                ClubImageUrl = app.BlobStorage.CreateOwnedBlobUrl("clubs", "club.png"),
                Email = $"{name.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}@example.com"
            })));

        if (response.StatusCode != HttpStatusCode.Created)
            throw new Xunit.Sdk.XunitException(await app.DescribeFailureAsync(response));

        return (await app.ReadApiResponseAsync<ClubApiModel>(response)).Data!;
    }

    /// <summary>Creates and publishes a free, public event with room to spare.</summary>
    private static async Task<EventResponse> CreateEventAsync(
        AuthApiTestApp app,
        string accessToken,
        int clubId,
        int startsInDays = 6)
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
                name = $"Favourite Event D{startsInDays}",
                description = "A published event used for favourite integration coverage.",
                location = "Student Center",
                imageUrls = new[] { image.PublicUrl },
                isPrivate = false,
                maxParticipants = 10,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(startsInDays),
                endTime = DateTime.UtcNow.AddDays(startsInDays).AddHours(2),
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

    private static async Task FavouriteAsync(AuthApiTestApp app, string accessToken, int eventId)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{eventId}/favourite", accessToken));

        if (response.StatusCode != HttpStatusCode.Created)
            throw new Xunit.Sdk.XunitException(await app.DescribeFailureAsync(response));
    }

    private static async Task<EventFavouriteResponse> GetMyFavouriteStatusAsync(
        AuthApiTestApp app, string accessToken, int eventId)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get, $"/api/events/{eventId}/favourite/me", accessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await app.ReadApiResponseAsync<EventFavouriteResponse>(response)).Data!;
    }

    private static async Task<List<int>> GetMyFavouriteIdsAsync(AuthApiTestApp app, string accessToken)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get, "/api/events/me/favourites/ids", accessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await app.ReadApiResponseAsync<List<int>>(response)).Data!;
    }

    private static async Task<List<PinnedEventResponse>> GetMyPinnedAsync(AuthApiTestApp app, string accessToken)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get, "/api/events/me/pinned", accessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await app.ReadApiResponseAsync<List<PinnedEventResponse>>(response)).Data!;
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
