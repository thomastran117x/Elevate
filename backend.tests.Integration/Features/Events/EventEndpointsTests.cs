using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.events;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.registration.contracts.responses;
using backend.main.shared.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

namespace backend.tests.Integration.Features.Events;

public class EventEndpointsTests
{
    [Fact]
    public async Task EventLifecycle_ShouldPresignCreateFetchSearchUpdateAndDelete()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-organizer@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Events Club");
        var image = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id);

        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{club.Id}",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Board Game Night",
                description = "A strategic games meetup for campus players.",
                location = "Student Center",
                imageUrls = new[] { image.PublicUrl },
                isPrivate = false,
                maxParticipants = 40,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(7),
                endTime = DateTime.UtcNow.AddDays(7).AddHours(3),
                category = EventCategory.Gaming,
                venueName = "Main Hall",
                city = "Toronto",
                latitude = 43.6532,
                longitude = -79.3832,
                tags = new[] { "games", "strategy" }
            })));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await app.ReadApiResponseAsync<EventResponse>(created);
        var ev = createdBody.Data!;
        ev.Name.Should().Be("Board Game Night");
        ev.ImageUrls.Should().ContainSingle(url => url == image.PublicUrl);

        var detail = await app.Client.GetAsync($"/api/events/{ev.Id}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailBody = await app.ReadApiResponseAsync<EventResponse>(detail);
        detailBody.Data!.Club.Should().NotBeNull();
        detailBody.Data.Club!.Id.Should().Be(club.Id);

        var byClub = await app.Client.GetAsync($"/api/events/clubs/{club.Id}?page=1&pageSize=20");
        byClub.StatusCode.Should().Be(HttpStatusCode.OK);
        var byClubBody = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(byClub);
        byClubBody.Data!.Items.Should().ContainSingle(item => item.Id == ev.Id);

        var search = await app.Client.PostAsJsonAsync("/api/events/search", new
        {
            query = "board game",
            page = 1,
            pageSize = 20
        });
        search.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchBody = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(search);
        searchBody.Data!.Items.Should().Contain(item => item.Id == ev.Id);

        var batch = await app.Client.GetAsync($"/api/events/batch?ids={ev.Id}");
        batch.StatusCode.Should().Be(HttpStatusCode.OK);
        var batchBody = await app.ReadApiResponseAsync<IEnumerable<EventResponse>>(batch);
        batchBody.Data.Should().ContainSingle(item => item.Id == ev.Id);

        var updated = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/events/{ev.Id}",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Advanced Board Game Night",
                description = "A strategic games meetup for advanced campus players.",
                location = "Innovation Hub",
                imageUrls = new[] { image.PublicUrl },
                isPrivate = false,
                maxParticipants = 50,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(8),
                endTime = DateTime.UtcNow.AddDays(8).AddHours(2),
                category = EventCategory.Gaming,
                venueName = "Room 201",
                city = "Toronto",
                latitude = 43.6532,
                longitude = -79.3832,
                tags = new[] { "games", "advanced" }
            })));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedBody = await app.ReadApiResponseAsync<EventResponse>(updated);
        updatedBody.Data!.Name.Should().Be("Advanced Board Game Night");
        updatedBody.Data.Location.Should().Be("Innovation Hub");

        var deleted = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/events/{ev.Id}",
            organizerSession.AccessToken));
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);

        var missing = await app.Client.GetAsync($"/api/events/{ev.Id}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EventImageEndpoints_ShouldAddAndRemoveImages()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-images@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Media Club");
        var firstImage = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id);
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Photo Walk", firstImage.PublicUrl);

        var secondImage = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id, ev.Id);
        var added = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/images",
            organizerSession.AccessToken,
            JsonContent.Create(new { imageUrl = secondImage.PublicUrl })));
        added.StatusCode.Should().Be(HttpStatusCode.Created);
        var addedBody = await app.ReadApiResponseAsync<EventImageApiModel>(added);

        var detailAfterAdd = await app.Client.GetAsync($"/api/events/{ev.Id}");
        var detailAfterAddBody = await app.ReadApiResponseAsync<EventResponse>(detailAfterAdd);
        detailAfterAddBody.Data!.ImageUrls.Should().HaveCount(2);

        var removed = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/events/{ev.Id}/images/{addedBody.Data!.Id}",
            organizerSession.AccessToken));
        removed.StatusCode.Should().Be(HttpStatusCode.OK);

        var detailAfterRemove = await app.Client.GetAsync($"/api/events/{ev.Id}");
        var detailAfterRemoveBody = await app.ReadApiResponseAsync<EventResponse>(detailAfterRemove);
        detailAfterRemoveBody.Data!.ImageUrls.Should().ContainSingle(url => url == firstImage.PublicUrl);
    }

    [Fact]
    public async Task RegistrationEndpoints_ShouldRegisterCheckListAndBatchUnregister()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-registration-organizer@example.com", "Organizer");
        var (participantSession, participant) = await CreateUserSessionAsync(app, "events-registration-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Registration Club");
        var firstEvent = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Networking Night",
            (await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id)).PublicUrl);
        var secondEvent = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Pitch Workshop",
            (await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id)).PublicUrl);

        var before = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{firstEvent.Id}/registrations/me",
            participantSession.AccessToken));
        before.StatusCode.Should().Be(HttpStatusCode.OK);
        (await before.Content.ReadAsStringAsync()).Should().Contain("\"isRegistered\":false");

        var registered = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{firstEvent.Id}/register",
            participantSession.AccessToken));
        registered.StatusCode.Should().Be(HttpStatusCode.Created);

        var check = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{firstEvent.Id}/registrations/me",
            participantSession.AccessToken));
        check.StatusCode.Should().Be(HttpStatusCode.OK);
        (await check.Content.ReadAsStringAsync()).Should().Contain("\"isRegistered\":true");

        var eventRegistrations = await app.Client.GetAsync($"/api/events/{firstEvent.Id}/registrations");
        eventRegistrations.StatusCode.Should().Be(HttpStatusCode.OK);
        var eventRegistrationsBody = await app.ReadApiResponseAsync<IEnumerable<EventRegistrationResponse>>(eventRegistrations);
        eventRegistrationsBody.Data.Should().ContainSingle(entry => entry.UserId == participant!.Id);

        var userRegistrations = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/users/{participant!.Id}/events/registered",
            participantSession.AccessToken));
        userRegistrations.StatusCode.Should().Be(HttpStatusCode.OK);
        var userRegistrationsBody = await app.ReadApiResponseAsync<IEnumerable<EventRegistrationResponse>>(userRegistrations);
        userRegistrationsBody.Data.Should().ContainSingle(entry => entry.EventId == firstEvent.Id);

        var batchRegister = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/batch/register",
            participantSession.AccessToken,
            JsonContent.Create(new { eventIds = new[] { firstEvent.Id, secondEvent.Id } })));
        batchRegister.StatusCode.Should().Be((HttpStatusCode)207);
        var batchRegisterBody = await app.ReadApiResponseAsync<BatchRegistrationResultResponse>(batchRegister);
        batchRegisterBody.Data!.Succeeded.Should().Contain(secondEvent.Id);
        batchRegisterBody.Data.Failed.Should().ContainSingle(failure => failure.EventId == firstEvent.Id);

        var batchUnregister = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            "/api/events/batch/register",
            participantSession.AccessToken,
            JsonContent.Create(new { eventIds = new[] { firstEvent.Id, secondEvent.Id } })));
        batchUnregister.StatusCode.Should().Be((HttpStatusCode)207);
        var batchUnregisterBody = await app.ReadApiResponseAsync<BatchRegistrationResultResponse>(batchUnregister);
        batchUnregisterBody.Data!.Succeeded.Should().Contain([firstEvent.Id, secondEvent.Id]);
    }

    [Fact]
    public async Task PrivateEvent_ShouldBeHiddenFromAnonymousUsers()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-private-organizer@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Private Events Club");
        var image = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id);

        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{club.Id}",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Secret Planning Session",
                description = "A private planning session for invited staff members only.",
                location = "Conference Room",
                imageUrls = new[] { image.PublicUrl },
                isPrivate = true,
                maxParticipants = 10,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(5),
                endTime = DateTime.UtcNow.AddDays(5).AddHours(2),
                category = EventCategory.Other,
                venueName = "North Wing",
                city = "Toronto",
                tags = new[] { "private" }
            })));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await app.ReadApiResponseAsync<EventResponse>(created);

        var anonymousFetch = await app.Client.GetAsync($"/api/events/{createdBody.Data!.Id}");
        anonymousFetch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<(AuthenticatedSessionResponse Session, backend.main.features.profile.User? User)> CreateUserSessionAsync(
        AuthApiTestApp app,
        string email,
        string role = "Participant")
    {
        var session = await app.SignUpAndVerifyByTokenAsync(
            email,
            role: role,
            transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync(email);
        return (session, user);
    }

    private static async Task<ClubApiModel> CreateClubAsync(AuthApiTestApp app, string accessToken, string name)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(name), "Name" },
            { new StringContent("Event testing group"), "Description" },
            { new StringContent("social"), "Clubtype" },
            { new StringContent($"{name.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}@example.com"), "Email" }
        };

        var imageBytes = new ByteArrayContent("club-image"u8.ToArray());
        imageBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(imageBytes, "ClubImage", "club.png");

        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs",
            accessToken,
            content));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await app.ReadApiResponseAsync<ClubApiModel>(response)).Data!;
    }

    private static async Task<PresignedUploadResponse> CreatePendingImageAsync(
        AuthApiTestApp app,
        string accessToken,
        int clubId,
        int? eventId = null)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/images/presigned-url",
            accessToken,
            JsonContent.Create(new
            {
                clubId,
                eventId,
                fileName = "poster.png",
                contentType = "image/png"
            })));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await app.ReadApiResponseAsync<PresignedUploadResponse>(response)).Data!;
    }

    private static async Task<EventResponse> CreateEventAsync(
        AuthApiTestApp app,
        string accessToken,
        int clubId,
        string name,
        string imageUrl)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{clubId}",
            accessToken,
            JsonContent.Create(new
            {
                name,
                description = "A detailed event description for integration testing coverage.",
                location = "Student Center",
                imageUrls = new[] { imageUrl },
                isPrivate = false,
                maxParticipants = 30,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(6),
                endTime = DateTime.UtcNow.AddDays(6).AddHours(2),
                category = EventCategory.Other,
                venueName = "Room A",
                city = "Toronto",
                tags = new[] { "testing" }
            })));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await app.ReadApiResponseAsync<EventResponse>(response)).Data!;
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
        public int Id { get; init; }
    }

    private sealed class EventImageApiModel
    {
        public int Id { get; init; }
        public string ImageUrl { get; init; } = string.Empty;
        public int SortOrder { get; init; }
    }
}
