using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.staff;
using backend.main.features.events;
using backend.main.features.events.contracts.responses;
using backend.main.features.payment;
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
        createdBody.Data!.LifecycleState.Should().Be(EventLifecycleState.Draft);
        var ev = await PublishEventAsync(app, organizerSession.AccessToken, createdBody.Data!.Id);
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
    public async Task DraftEvent_ShouldStayPrivateUntilPublished_AndAppearInManageEndpoints()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, organizer) = await CreateUserSessionAsync(app, "events-draft-owner@example.com", "Organizer");
        var (managerSession, manager) = await CreateUserSessionAsync(app, "events-draft-manager@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Draft Workflow Club");
        await app.AddClubStaffAsync(club.Id, manager!.Id, organizer!.Id, ClubStaffRole.Manager);

        var createResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{club.Id}",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Draft First Showcase",
                description = "This event starts life as a private organizer draft.",
                location = "Student Center",
                imageUrls = new[] { (await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id)).PublicUrl },
                isPrivate = false,
                maxParticipants = 50,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(7),
                endTime = DateTime.UtcNow.AddDays(7).AddHours(2),
                category = EventCategory.Other
            })));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await app.ReadApiResponseAsync<EventResponse>(createResponse)).Data!;
        created.LifecycleState.Should().Be(EventLifecycleState.Draft);

        var publicDetailBeforePublish = await app.Client.GetAsync($"/api/events/{created.Id}");
        publicDetailBeforePublish.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var managerDetail = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{created.Id}/manage",
            managerSession.AccessToken));
        managerDetail.StatusCode.Should().Be(HttpStatusCode.OK);

        var managerList = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/clubs/{club.Id}/manage?page=1&pageSize=20",
            managerSession.AccessToken));
        managerList.StatusCode.Should().Be(HttpStatusCode.OK);
        var managerListBody = await app.ReadApiResponseAsync<PagedResponse<ManagedEventResponse>>(managerList);
        managerListBody.Data!.Items.Should().ContainSingle(item => item.Id == created.Id);

        await PublishEventAsync(app, organizerSession.AccessToken, created.Id);

        var publicDetailAfterPublish = await app.Client.GetAsync($"/api/events/{created.Id}");
        publicDetailAfterPublish.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ArchivedEvent_ShouldBeHiddenFromPublicDetail_ButRemainManageable()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-archive-owner@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Archive Workflow Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Archive Candidate");

        var archiveResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/archive",
            organizerSession.AccessToken,
            JsonContent.Create(new { })));
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var archived = (await app.ReadApiResponseAsync<ManagedEventResponse>(archiveResponse)).Data!;
        archived.LifecycleState.Should().Be(EventLifecycleState.Archived);

        var publicDetail = await app.Client.GetAsync($"/api/events/{ev.Id}");
        publicDetail.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var manageDetail = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{ev.Id}/manage",
            organizerSession.AccessToken));
        manageDetail.StatusCode.Should().Be(HttpStatusCode.OK);
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
        var created = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Secret Planning Session",
            isPrivate: true);

        var anonymousFetch = await app.Client.GetAsync($"/api/events/{created.Id}");
        anonymousFetch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PrivateEvent_ShouldBeHiddenFromUnrelatedAuthenticatedUsers()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-private-owner@example.com", "Organizer");
        var (outsiderSession, _) = await CreateUserSessionAsync(app, "events-private-outsider@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Members Only Club");
        var created = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Members Only Roundtable",
            isPrivate: true);

        var detail = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{created.Id}",
            outsiderSession.AccessToken));

        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PrivateEvent_ShouldOnlyAppearInBatchResultsForAuthorizedUsers()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, organizer) = await CreateUserSessionAsync(app, "events-batch-organizer@example.com", "Organizer");
        var (outsiderSession, outsider) = await CreateUserSessionAsync(app, "events-batch-outsider@example.com");
        var (inviteeSession, invitee) = await CreateUserSessionAsync(app, "events-batch-invitee@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Batch Visibility Club");
        var publicEvent = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Open Workshop");
        var privateEvent = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Hidden Strategy Session",
            isPrivate: true);

        await app.AddAcceptedInvitationAsync(privateEvent.Id, invitee!.Id, invitee.Email);

        var outsiderBatch = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/batch?ids={publicEvent.Id},{privateEvent.Id}",
            outsiderSession.AccessToken));
        outsiderBatch.StatusCode.Should().Be(HttpStatusCode.OK);
        var outsiderBody = await app.ReadApiResponseAsync<IEnumerable<EventResponse>>(outsiderBatch);
        outsiderBody.Data.Should().ContainSingle(item => item.Id == publicEvent.Id);
        outsiderBody.Data.Should().NotContain(item => item.Id == privateEvent.Id);

        var inviteeBatch = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/batch?ids={publicEvent.Id},{privateEvent.Id}",
            inviteeSession.AccessToken));
        inviteeBatch.StatusCode.Should().Be(HttpStatusCode.OK);
        var inviteeBody = await app.ReadApiResponseAsync<IEnumerable<EventResponse>>(inviteeBatch);
        inviteeBody.Data.Should().Contain(item => item.Id == publicEvent.Id);
        inviteeBody.Data.Should().Contain(item => item.Id == privateEvent.Id);

        organizer.Should().NotBeNull();
        outsider.Should().NotBeNull();
    }

    [Fact]
    public async Task PrivateEvent_ShouldAllowAuthorizedViewersAcrossSupportedAccessPaths()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, organizer) = await CreateUserSessionAsync(app, "events-view-organizer@example.com", "Organizer");
        var (staffSession, staffUser) = await CreateUserSessionAsync(app, "events-view-staff@example.com");
        var (inviteeSession, inviteeUser) = await CreateUserSessionAsync(app, "events-view-invitee@example.com");
        var (registeredSession, registeredUser) = await CreateUserSessionAsync(app, "events-view-registered@example.com");
        var (payerSession, payerUser) = await CreateUserSessionAsync(app, "events-view-payer@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Private Access Club");
        var ev = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Private Access Summit",
            isPrivate: true);

        await app.AddClubStaffAsync(club.Id, staffUser!.Id, organizer!.Id);
        await app.AddAcceptedInvitationAsync(ev.Id, inviteeUser!.Id, inviteeUser.Email);
        await app.AddRegistrationAsync(ev.Id, registeredUser!.Id);
        await app.AddPaymentAsync(ev.Id, payerUser!.Id, PaymentStatus.Pending);

        foreach (var accessToken in new[]
                 {
                     organizerSession.AccessToken,
                     staffSession.AccessToken,
                     inviteeSession.AccessToken,
                     registeredSession.AccessToken,
                     payerSession.AccessToken
                 })
        {
            var detail = await app.Client.SendAsync(CreateAuthorizedRequest(
                HttpMethod.Get,
                $"/api/events/{ev.Id}",
                accessToken));

            detail.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await app.ReadApiResponseAsync<EventResponse>(detail);
            body.Data.Should().NotBeNull();
            body.Data!.Id.Should().Be(ev.Id);
        }
    }

    [Fact]
    public async Task ClubEventsEndpoint_ShouldKeepPrivateEventsOutOfPublicListing()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-clublist-organizer@example.com", "Organizer");
        var (inviteeSession, invitee) = await CreateUserSessionAsync(app, "events-clublist-invitee@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Club Listing Visibility");
        var publicEvent = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Public Demo Day");
        var privateEvent = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Private Planning Board",
            isPrivate: true);

        await app.AddAcceptedInvitationAsync(privateEvent.Id, invitee!.Id, invitee.Email);

        var anonymousListing = await app.Client.GetAsync($"/api/events/clubs/{club.Id}?page=1&pageSize=20");
        anonymousListing.StatusCode.Should().Be(HttpStatusCode.OK);
        var anonymousBody = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(anonymousListing);
        anonymousBody.Data!.Items.Should().ContainSingle(item => item.Id == publicEvent.Id);
        anonymousBody.Data.Items.Should().NotContain(item => item.Id == privateEvent.Id);

        var authenticatedListing = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/clubs/{club.Id}?page=1&pageSize=20",
            inviteeSession.AccessToken));
        authenticatedListing.StatusCode.Should().Be(HttpStatusCode.OK);
        var authenticatedBody = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(authenticatedListing);
        authenticatedBody.Data!.Items.Should().ContainSingle(item => item.Id == publicEvent.Id);
        authenticatedBody.Data.Items.Should().NotContain(item => item.Id == privateEvent.Id);
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
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs",
            accessToken,
            JsonContent.Create(new
            {
                Name = name,
                Description = "Event testing group",
                Clubtype = "social",
                ClubImageUrl = app.BlobStorage.CreateOwnedBlobUrl("clubs", "club.png"),
                Email = $"{name.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}@example.com"
            })));
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
        string? imageUrl = null,
        bool isPrivate = false)
    {
        var resolvedImageUrl = imageUrl ?? (await CreatePendingImageAsync(app, accessToken, clubId)).PublicUrl;
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{clubId}",
            accessToken,
            JsonContent.Create(new
            {
                name,
                description = "A detailed event description for integration testing coverage.",
                location = "Student Center",
                imageUrls = new[] { resolvedImageUrl },
                isPrivate,
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
        var created = (await app.ReadApiResponseAsync<EventResponse>(response)).Data!;
        return await PublishEventAsync(app, accessToken, created.Id);
    }

    private static async Task<EventResponse> PublishEventAsync(
        AuthApiTestApp app,
        string accessToken,
        int eventId)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{eventId}/publish",
            accessToken,
            JsonContent.Create(new { })));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await app.ReadApiResponseAsync<ManagedEventResponse>(response)).Data switch
        {
            null => throw new InvalidOperationException("Publish response did not include event data."),
            var managed => new EventResponse
            {
                Id = managed.Id,
                Name = managed.Name ?? string.Empty,
                Description = managed.Description ?? string.Empty,
                Location = managed.Location ?? string.Empty,
                ImageUrls = managed.ImageUrls,
                IsPrivate = managed.IsPrivate,
                MaxParticipants = managed.MaxParticipants ?? 0,
                RegisterCost = managed.RegisterCost,
                StartTime = managed.StartTime.HasValue ? managed.StartTime.Value : DateTime.UtcNow,
                EndTime = managed.EndTime,
                ClubId = managed.ClubId,
                CurrentVersionNumber = managed.CurrentVersionNumber,
                CreatedAt = managed.CreatedAt,
                LifecycleState = managed.LifecycleState,
                Status = managed.Status ?? EventStatus.Upcoming,
                Category = managed.Category,
                VenueName = managed.VenueName,
                City = managed.City,
                Latitude = managed.Latitude,
                Longitude = managed.Longitude,
                Tags = managed.Tags,
                RegistrationCount = managed.RegistrationCount
            }
        };
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

    [Fact]
    public async Task Registration_ShouldBeRejected_WhenEventHasAlreadyStarted()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "reg-started-org@example.com", "Organizer");
        var (participantSession, _) = await CreateUserSessionAsync(app, "reg-started-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Started Event Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Past Start Event");
        await app.SetEventStartTimeToPast(ev.Id);

        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/register",
            participantSession.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().ContainAny("started", "already started");
    }

    [Fact]
    public async Task Unregistration_ShouldBeRejected_WhenEventHasAlreadyStarted()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "unreg-started-org@example.com", "Organizer");
        var (participantSession, participant) = await CreateUserSessionAsync(app, "unreg-started-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Unregister Started Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Locked Event");
        await app.AddRegistrationAsync(ev.Id, participant!.Id);
        await app.SetEventStartTimeToPast(ev.Id);

        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/events/{ev.Id}/register",
            participantSession.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().ContainAny("started", "already started");
    }

    [Fact]
    public async Task Registration_ShouldSucceed_WhenMaxParticipantsIsZero_Unlimited()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "unlimited-org@example.com", "Organizer");
        var (userASession, _) = await CreateUserSessionAsync(app, "unlimited-user-a@example.com");
        var (userBSession, _) = await CreateUserSessionAsync(app, "unlimited-user-b@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Unlimited Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Unlimited Event");
        // SetMaxParticipants(0) must be called after publish (publish gate requires >= 1)
        await app.SetMaxParticipants(ev.Id, 0);

        var regA = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{ev.Id}/register", userASession.AccessToken));
        regA.StatusCode.Should().Be(HttpStatusCode.Created);

        var regB = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{ev.Id}/register", userBSession.AccessToken));
        regB.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Registration_ShouldSucceed_WhenReRegisteringAfterCancellation()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "reregister-org@example.com", "Organizer");
        var (participantSession, _) = await CreateUserSessionAsync(app, "reregister-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Reregister Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Reregister Event");

        var reg1 = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{ev.Id}/register", participantSession.AccessToken));
        reg1.StatusCode.Should().Be(HttpStatusCode.Created);

        var unreg = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete, $"/api/events/{ev.Id}/register", participantSession.AccessToken));
        unreg.StatusCode.Should().Be(HttpStatusCode.OK);

        var reg2 = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{ev.Id}/register", participantSession.AccessToken));
        reg2.StatusCode.Should().Be(HttpStatusCode.Created);

        var check = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get, $"/api/events/{ev.Id}/registrations/me", participantSession.AccessToken));
        check.StatusCode.Should().Be(HttpStatusCode.OK);
        (await check.Content.ReadAsStringAsync()).Should().Contain("\"isRegistered\":true");
    }

    [Fact]
    public async Task Registration_ShouldBeRejected_WithCorrectMessage_WhenEventHasEnded()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "ended-org@example.com", "Organizer");
        var (participantSession, _) = await CreateUserSessionAsync(app, "ended-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Ended Event Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Ended Event");
        await app.SetEventEndTimeToPast(ev.Id);

        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{ev.Id}/register", participantSession.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Event is full");
        body.Should().ContainAny("ended", "already ended");
    }

    [Fact]
    public async Task Registration_ShouldBeRejected_WhenEventIsAtCapacity()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, organizer) = await CreateUserSessionAsync(app, "capacity-org@example.com", "Organizer");
        var (participantSession, _) = await CreateUserSessionAsync(app, "capacity-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Capacity Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Full Event");
        await app.SetMaxParticipants(ev.Id, 1);
        await app.AddRegistrationAsync(ev.Id, organizer!.Id);

        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{ev.Id}/register", participantSession.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("full");
    }

    [Fact]
    public async Task Registration_CancelledRegistration_ShouldNotAppearInRegistrationsList()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "cancel-list-org@example.com", "Organizer");
        var (participantSession, participant) = await CreateUserSessionAsync(app, "cancel-list-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Soft Delete Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Soft Delete Event");

        await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post, $"/api/events/{ev.Id}/register", participantSession.AccessToken));

        await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete, $"/api/events/{ev.Id}/register", participantSession.AccessToken));

        var registrations = await app.Client.GetAsync($"/api/events/{ev.Id}/registrations");
        registrations.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await app.ReadApiResponseAsync<IEnumerable<EventRegistrationResponse>>(registrations);
        body.Data.Should().NotContain(r => r.UserId == participant!.Id);
    }

    [Fact]
    public async Task Registration_ShouldPersistDetailFields_Notes_Phone_Dietary()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "details-org@example.com", "Organizer");
        var (participantSession, participant) = await CreateUserSessionAsync(app, "details-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Details Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Details Event");

        var reg = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/register",
            participantSession.AccessToken,
            JsonContent.Create(new
            {
                notes = "Please seat me near the front.",
                phoneNumber = "416-555-0123",
                dietaryNeeds = "Vegetarian, no nuts"
            })));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);

        // Organizer sees full PII
        var registrations = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get, $"/api/events/{ev.Id}/registrations", organizerSession.AccessToken));
        var body = await app.ReadApiResponseAsync<IEnumerable<EventRegistrationResponse>>(registrations);
        var entry = body.Data.Should().ContainSingle(r => r.UserId == participant!.Id).Subject;
        entry.Notes.Should().Be("Please seat me near the front.");
        entry.PhoneNumber.Should().Be("416-555-0123");
        entry.DietaryNeeds.Should().Be("Vegetarian, no nuts");
        entry.Status.Should().Be("Active");

        // Anonymous caller sees the registration but not the PII
        var publicRegistrations = await app.Client.GetAsync($"/api/events/{ev.Id}/registrations");
        var publicBody = await app.ReadApiResponseAsync<IEnumerable<EventRegistrationResponse>>(publicRegistrations);
        var publicEntry = publicBody.Data.Should().ContainSingle(r => r.UserId == participant!.Id).Subject;
        publicEntry.Notes.Should().BeNull();
        publicEntry.PhoneNumber.Should().BeNull();
        publicEntry.DietaryNeeds.Should().BeNull();
    }

    [Fact]
    public async Task Registration_ShouldUpdateDetails_WhenPatchIsCalled()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "update-details-org@example.com", "Organizer");
        var (participantSession, participant) = await CreateUserSessionAsync(app, "update-details-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Update Details Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Update Details Event");

        await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/register",
            participantSession.AccessToken,
            JsonContent.Create(new { notes = "Original note" })));

        var patch = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"/api/events/{ev.Id}/register",
            participantSession.AccessToken,
            JsonContent.Create(new
            {
                notes = "Updated note",
                phoneNumber = "647-555-9999",
                dietaryNeeds = "Gluten free"
            })));
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        // Use organizer auth so PII fields are included in the response
        var registrations = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get, $"/api/events/{ev.Id}/registrations", organizerSession.AccessToken));
        var body = await app.ReadApiResponseAsync<IEnumerable<EventRegistrationResponse>>(registrations);
        var entry = body.Data.Should().ContainSingle(r => r.UserId == participant!.Id).Subject;
        entry.Notes.Should().Be("Updated note");
        entry.PhoneNumber.Should().Be("647-555-9999");
        entry.DietaryNeeds.Should().Be("Gluten free");
        entry.Status.Should().Be("Active");
    }

    [Fact]
    public async Task Registration_UpdateDetails_ShouldFail_WhenNotRegistered()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "update-notfound-org@example.com", "Organizer");
        var (participantSession, _) = await CreateUserSessionAsync(app, "update-notfound-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Notfound Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Notfound Event");

        var patch = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"/api/events/{ev.Id}/register",
            participantSession.AccessToken,
            JsonContent.Create(new { notes = "Should fail" })));

        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRegisteredEvents_ShouldForbid_WhenCallerViewsAnotherUsersRegistrations()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (sessionA, userA) = await CreateUserSessionAsync(app, "idor-user-a@example.com");
        var (sessionB, _) = await CreateUserSessionAsync(app, "idor-user-b@example.com");

        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/users/{userA!.Id}/events/registered",
            sessionB.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetRegistrations_ShouldRedactPii_ForAnonymousCaller()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "pii-redact-org@example.com", "Organizer");
        var (participantSession, participant) = await CreateUserSessionAsync(app, "pii-redact-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "PII Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "PII Event");

        await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/register",
            participantSession.AccessToken,
            JsonContent.Create(new { notes = "Secret note", phoneNumber = "416-000-0000" })));

        var response = await app.Client.GetAsync($"/api/events/{ev.Id}/registrations");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await app.ReadApiResponseAsync<IEnumerable<EventRegistrationResponse>>(response);
        var entry = body.Data.Should().ContainSingle(r => r.UserId == participant!.Id).Subject;
        entry.Notes.Should().BeNull();
        entry.PhoneNumber.Should().BeNull();
        entry.DietaryNeeds.Should().BeNull();
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
