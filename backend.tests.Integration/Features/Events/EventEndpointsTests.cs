using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.staff;
using backend.main.features.events;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.images;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.events.versions;
using backend.main.features.events.versions.contracts.responses;
using backend.main.features.payment;
using backend.main.features.events.registration.contracts.responses;
using backend.main.infrastructure.database.core;
using backend.main.infrastructure.elasticsearch;
using backend.main.shared.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        var persistedEvent = await app.QueryDbAsync(db => db.Events.SingleOrDefaultAsync(e => e.Id == ev.Id));
        persistedEvent.Should().NotBeNull();
        persistedEvent!.Name.Should().Be("Board Game Night");
        persistedEvent.ClubId.Should().Be(club.Id);
        persistedEvent.LifecycleState.Should().Be(EventLifecycleState.Published);

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

        var persistedAfterUpdate = await app.QueryDbAsync(db => db.Events.SingleAsync(e => e.Id == ev.Id));
        persistedAfterUpdate.Name.Should().Be("Advanced Board Game Night");
        persistedAfterUpdate.Location.Should().Be("Innovation Hub");
        persistedAfterUpdate.maxParticipants.Should().Be(50);

        var deleted = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/events/{ev.Id}",
            organizerSession.AccessToken));
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.QueryDbAsync(db => db.Events.AnyAsync(e => e.Id == ev.Id))).Should().BeFalse();

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

        var persistedImage = await app.QueryDbAsync(db =>
            db.EventImages.SingleOrDefaultAsync(i => i.Id == addedBody.Data!.Id));
        persistedImage.Should().NotBeNull();
        persistedImage!.EventId.Should().Be(ev.Id);
        persistedImage.ImageUrl.Should().Be(secondImage.PublicUrl);
        (await app.QueryDbAsync(db => db.EventImages.CountAsync(i => i.EventId == ev.Id))).Should().Be(2);

        var removed = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/events/{ev.Id}/images/{addedBody.Data!.Id}",
            organizerSession.AccessToken));
        removed.StatusCode.Should().Be(HttpStatusCode.OK);

        var detailAfterRemove = await app.Client.GetAsync($"/api/events/{ev.Id}");
        var detailAfterRemoveBody = await app.ReadApiResponseAsync<EventResponse>(detailAfterRemove);
        detailAfterRemoveBody.Data!.ImageUrls.Should().ContainSingle(url => url == firstImage.PublicUrl);

        (await app.QueryDbAsync(db => db.EventImages.AnyAsync(i => i.Id == addedBody.Data!.Id))).Should().BeFalse();
    }

    [Fact]
    public async Task EventImageEndpoints_ShouldRejectUploadsForDifferentOrganizerEventOrMissingIntent()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, owner) = await CreateUserSessionAsync(app, "events-images-owner@example.com", "Organizer");
        var (managerSession, manager) = await CreateUserSessionAsync(app, "events-images-manager@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Intent Validation Club");
        await app.AddClubStaffAsync(club.Id, manager!.Id, owner!.Id, ClubStaffRole.Manager);

        var firstEvent = await CreateEventAsync(app, ownerSession.AccessToken, club.Id, "Intent Event One");
        var secondEvent = await CreateEventAsync(app, ownerSession.AccessToken, club.Id, "Intent Event Two");

        var ownerPending = await CreatePendingImageAsync(app, ownerSession.AccessToken, club.Id, firstEvent.Id);
        var wrongOrganizer = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{firstEvent.Id}/images",
            managerSession.AccessToken,
            JsonContent.Create(new { imageUrl = ownerPending.PublicUrl })));
        wrongOrganizer.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var eventScopedPending = await CreatePendingImageAsync(app, ownerSession.AccessToken, club.Id, firstEvent.Id);
        var wrongEvent = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{secondEvent.Id}/images",
            ownerSession.AccessToken,
            JsonContent.Create(new { imageUrl = eventScopedPending.PublicUrl })));
        wrongEvent.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var expiredPending = await CreatePendingImageAsync(app, ownerSession.AccessToken, club.Id, firstEvent.Id);
        await app.Cache.DeleteKeyAsync(GetImageUploadIntentKey(expiredPending.PublicUrl));

        var missingIntent = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{firstEvent.Id}/images",
            ownerSession.AccessToken,
            JsonContent.Create(new { imageUrl = expiredPending.PublicUrl })));
        missingIntent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

        (await app.QueryDbAsync(db => db.Events.Where(e => e.Id == created.Id).Select(e => e.LifecycleState).SingleAsync()))
            .Should().Be(EventLifecycleState.Draft);

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

        (await app.QueryDbAsync(db => db.Events.Where(e => e.Id == created.Id).Select(e => e.LifecycleState).SingleAsync()))
            .Should().Be(EventLifecycleState.Published);

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

        (await app.QueryDbAsync(db => db.Events.Where(e => e.Id == ev.Id).Select(e => e.LifecycleState).SingleAsync()))
            .Should().Be(EventLifecycleState.Archived);

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

        var persistedRegistration = await app.QueryDbAsync(db =>
            db.EventRegistrations.SingleOrDefaultAsync(r => r.EventId == firstEvent.Id && r.UserId == participant!.Id));
        persistedRegistration.Should().NotBeNull();
        persistedRegistration!.Status.Should().Be(RegistrationStatus.Active);
        (await app.QueryDbAsync(db => db.Events.Where(e => e.Id == firstEvent.Id).Select(e => e.RegistrationCount).SingleAsync()))
            .Should().Be(1);

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

        (await app.QueryDbAsync(db => db.EventRegistrations
            .Where(r => r.EventId == firstEvent.Id && r.UserId == participant!.Id)
            .Select(r => r.Status)
            .SingleAsync()))
            .Should().Be(RegistrationStatus.Cancelled);
        (await app.QueryDbAsync(db => db.Events.Where(e => e.Id == firstEvent.Id).Select(e => e.RegistrationCount).SingleAsync()))
            .Should().Be(0);
    }

    [Fact]
    public async Task EventValidationEndpoints_ShouldRejectInvalidDraftAndRegistrationPayloads()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-validation-owner@example.com", "Organizer");
        var (participantSession, participant) = await CreateUserSessionAsync(app, "events-validation-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Validation Club");
        var image = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id);
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Validation Event");

        var invalidDraft = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/clubs/{club.Id}/drafts",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Bad Draft",
                description = "A private paid draft should be rejected.",
                location = "Room Z",
                imageUrls = new[] { image.PublicUrl },
                isPrivate = true,
                maxParticipants = 10,
                registerCost = 100,
                startTime = DateTime.UtcNow.AddDays(3),
                endTime = DateTime.UtcNow.AddDays(3).AddHours(1),
                category = EventCategory.Other
            })));
        invalidDraft.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidRegister = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/register",
            participantSession.AccessToken,
            JsonContent.Create(new
            {
                notes = new string('n', 501)
            })));
        invalidRegister.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var register = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/register",
            participantSession.AccessToken,
            JsonContent.Create(new { notes = "Registered for validation coverage." })));
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        var invalidPatch = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"/api/events/{ev.Id}/register",
            participantSession.AccessToken,
            JsonContent.Create(new
            {
                phoneNumber = new string('1', 31)
            })));
        invalidPatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidRegistrationsPage = await app.Client.GetAsync($"/api/events/{ev.Id}/registrations?page=0&pageSize=20");
        invalidRegistrationsPage.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidRegisteredEventsPage = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/users/{participant!.Id}/events/registered?page=1&pageSize=101",
            participantSession.AccessToken));
        invalidRegisteredEventsPage.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidBatchRegister = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/batch/register",
            participantSession.AccessToken,
            JsonContent.Create(new { eventIds = Array.Empty<int>() })));
        invalidBatchRegister.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidEventId = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/0/register",
            participantSession.AccessToken,
            JsonContent.Create(new { })));
        invalidEventId.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    [Fact]
    public async Task EventDiscoveryEndpoints_ShouldRejectInvalidPublicQueries()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-invalid-query-owner@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Invalid Query Club");
        await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Queryable Event");

        var invalidClubPage = await app.Client.GetAsync($"/api/events/clubs/{club.Id}?page=0&pageSize=20");
        invalidClubPage.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidClubPageSize = await app.Client.GetAsync($"/api/events/clubs/{club.Id}?page=1&pageSize=101");
        invalidClubPageSize.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var privateFilter = await app.Client.GetAsync("/api/events?isPrivate=true&page=1&pageSize=20");
        privateFilter.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var missingDistanceCoordinates = await app.Client.GetAsync("/api/events?sortBy=Distance&page=1&pageSize=20");
        missingDistanceCoordinates.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidBatchIds = await app.Client.GetAsync("/api/events/batch?ids=foo,bar");
        invalidBatchIds.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidSearch = await app.Client.PostAsJsonAsync("/api/events/search", new
        {
            query = "Queryable",
            sortBy = "Distance",
            page = 1,
            pageSize = 20,
            geo = new
            {
                lat = 43.65
            }
        });
        invalidSearch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EventSearchEndpoints_ShouldEscapeLiteralWildcards_AndExcludePrivateOrDraftMatches()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-search-visibility@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Search Visibility Club");
        var literalMatch = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Campus Search 100% Match");
        var wildcardDecoy = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Campus Search 100 Days");
        var privateMatch = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Campus Search 100% Private",
            isPrivate: true);

        var draftCreate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{club.Id}",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Campus Search 100% Draft",
                description = "Draft events should stay hidden from public search results.",
                location = "Student Center",
                imageUrls = new[] { (await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id)).PublicUrl },
                isPrivate = false,
                maxParticipants = 20,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(7),
                endTime = DateTime.UtcNow.AddDays(7).AddHours(2),
                category = EventCategory.Other,
                venueName = "Room Search",
                city = "Toronto",
                tags = new[] { "search" }
            })));
        draftCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        var draftEvent = (await app.ReadApiResponseAsync<EventResponse>(draftCreate)).Data!;

        var search = await app.Client.GetAsync("/api/events?search=100%25&page=1&pageSize=20");
        search.StatusCode.Should().Be(HttpStatusCode.OK);

        var searchBody = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(search);
        searchBody.Data!.Items.Select(item => item.Id).Should().Contain(literalMatch.Id);
        searchBody.Data.Items.Select(item => item.Id).Should().NotContain(wildcardDecoy.Id);
        searchBody.Data.Items.Select(item => item.Id).Should().NotContain(privateMatch.Id);
        searchBody.Data.Items.Select(item => item.Id).Should().NotContain(draftEvent.Id);
    }

    [Fact]
    public async Task EventSearchEndpoints_ShouldFilterByStatusAcrossGetAndPostSearch()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-search-status@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Search Status Club");
        var upcoming = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Status Search Upcoming");
        var ongoing = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Status Search Ongoing");
        var closed = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Status Search Closed");

        await app.SetEventStartTimeToPast(ongoing.Id);
        await app.SetEventEndTimeToPast(closed.Id);

        var upcomingSearch = await app.Client.GetAsync("/api/events?search=Status%20Search&status=Upcoming&page=1&pageSize=20");
        upcomingSearch.StatusCode.Should().Be(HttpStatusCode.OK);
        var upcomingBody = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(upcomingSearch);
        upcomingBody.Data!.Items.Select(item => item.Id).Should().ContainSingle().Which.Should().Be(upcoming.Id);

        var ongoingSearch = await app.Client.PostAsJsonAsync("/api/events/search", new
        {
            query = "Status Search",
            sortBy = EventSortBy.Relevance,
            page = 1,
            pageSize = 20,
            filters = new
            {
                status = EventStatus.Ongoing
            }
        });
        ongoingSearch.StatusCode.Should().Be(HttpStatusCode.OK);
        var ongoingBody = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(ongoingSearch);
        ongoingBody.Data!.Items.Select(item => item.Id).Should().ContainSingle().Which.Should().Be(ongoing.Id);

        var closedSearch = await app.Client.GetAsync("/api/events?search=Status%20Search&status=Closed&page=1&pageSize=20");
        closedSearch.StatusCode.Should().Be(HttpStatusCode.OK);
        var closedBody = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(closedSearch);
        closedBody.Data!.Items.Select(item => item.Id).Should().ContainSingle().Which.Should().Be(closed.Id);
    }

    [Fact]
    public async Task EventSearchEndpoints_ShouldSortByPopularity_AndPaginateResults()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-search-popularity@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Search Popularity Club");
        var mostPopular = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Popularity Search Alpha");
        var middlePopular = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Popularity Search Beta");
        var leastPopular = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Popularity Search Gamma");

        var userA = await app.SeedUserAsync("popularity-a@example.com");
        var userB = await app.SeedUserAsync("popularity-b@example.com");
        var userC = await app.SeedUserAsync("popularity-c@example.com");

        await app.AddRegistrationAsync(mostPopular.Id, userA.Id);
        await app.AddRegistrationAsync(mostPopular.Id, userB.Id);
        await app.AddRegistrationAsync(middlePopular.Id, userC.Id);

        var pageOne = await app.Client.PostAsJsonAsync("/api/events/search", new
        {
            query = "Popularity Search",
            sortBy = EventSortBy.Popularity,
            page = 1,
            pageSize = 3
        });
        pageOne.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageOneBody = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(pageOne);
        pageOneBody.Data!.Items.Select(item => item.Id).Should().ContainInOrder(
            mostPopular.Id,
            middlePopular.Id,
            leastPopular.Id);

        var pageTwo = await app.Client.PostAsJsonAsync("/api/events/search", new
        {
            query = "Popularity Search",
            sortBy = EventSortBy.Popularity,
            page = 2,
            pageSize = 1
        });
        pageTwo.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageTwoBody = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(pageTwo);
        pageTwoBody.Data!.TotalCount.Should().Be(3);
        pageTwoBody.Data.Page.Should().Be(2);
        pageTwoBody.Data.PageSize.Should().Be(1);
        pageTwoBody.Data.Items.Select(item => item.Id).Should().ContainSingle().Which.Should().Be(middlePopular.Id);
    }

    [Fact]
    public async Task EventSearchEndpoints_ShouldRejectInvalidTagAndGeoParameters()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var tooManyTags = await app.Client.GetAsync("/api/events?tags=one,two,three,four,five,six&page=1&pageSize=20");
        tooManyTags.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidLatitude = await app.Client.GetAsync("/api/events?lat=91&lng=-79.38&page=1&pageSize=20");
        invalidLatitude.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidRadius = await app.Client.PostAsJsonAsync("/api/events/search", new
        {
            query = "Search",
            page = 1,
            pageSize = 20,
            geo = new
            {
                lat = 43.65,
                lng = -79.38,
                radiusKm = 501
            }
        });
        invalidRadius.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidSearchTags = await app.Client.PostAsJsonAsync("/api/events/search", new
        {
            query = "Search",
            page = 1,
            pageSize = 20,
            filters = new
            {
                tags = new[] { "one", "two", "three", "four", "five", "six" }
            }
        });
        invalidSearchTags.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EventSearchEndpoints_ShouldFilterByCategoryAndTrimCityOnPostSearch()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-search-filters@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Search Filters Club");
        var matching = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Filter Search Match",
            category: EventCategory.Gaming,
            city: "Toronto");
        var wrongCategory = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Filter Search Wrong Category",
            category: EventCategory.Academic,
            city: "Toronto");
        var wrongCity = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Filter Search Wrong City",
            category: EventCategory.Gaming,
            city: "Ottawa");

        var search = await app.Client.PostAsJsonAsync("/api/events/search", new
        {
            query = "  Filter Search  ",
            page = 1,
            pageSize = 20,
            filters = new
            {
                category = EventCategory.Gaming,
                city = " Toronto "
            }
        });
        search.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(search);
        body.Data!.Items.Select(item => item.Id).Should().ContainSingle().Which.Should().Be(matching.Id);
        body.Data.Items.Select(item => item.Id).Should().NotContain(wrongCategory.Id);
        body.Data.Items.Select(item => item.Id).Should().NotContain(wrongCity.Id);
    }

    [Fact]
    public async Task EventSearchEndpoints_ShouldReturnServiceUnavailable_ForUnsupportedFallbackQueries()
    {
        await using var app = await AuthApiTestApp.CreateAsync(services =>
        {
            services.RemoveAll<IEventSearchService>();
            services.AddSingleton<IEventSearchService>(new UnavailableEventSearchService());
        });

        var tagSearch = await app.Client.GetAsync("/api/events?tags=testing&page=1&pageSize=20");
        tagSearch.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var distanceSearch = await app.Client.GetAsync("/api/events?lat=43.6532&lng=-79.3832&sortBy=Distance&page=1&pageSize=20");
        distanceSearch.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var geoSearch = await app.Client.PostAsJsonAsync("/api/events/search", new
        {
            query = "Search",
            page = 1,
            pageSize = 20,
            geo = new
            {
                lat = 43.6532,
                lng = -79.3832,
                radiusKm = 5
            }
        });
        geoSearch.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task EventSearchEndpoints_ShouldMapDistanceResultsAndNormalizeCriteria_WhenSearchServiceIsAvailable()
    {
        var searchService = new StubEventSearchService();

        await using var app = await AuthApiTestApp.CreateAsync(services =>
        {
            services.RemoveAll<IEventSearchService>();
            services.AddSingleton<IEventSearchService>(searchService);
        });

        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-search-service@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Search Service Club");

        var farther = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Service Search Farther",
            category: EventCategory.Gaming,
            city: "Toronto",
            latitude: 43.70,
            longitude: -79.40);
        var nearer = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Service Search Nearer",
            category: EventCategory.Gaming,
            city: "Toronto",
            latitude: 43.65,
            longitude: -79.38);

        searchService.Result = new EventSearchResult(
            [
                new EventSearchHit(nearer.Id, 0.8),
                new EventSearchHit(farther.Id, 4.2)
            ],
            2);

        var search = await app.Client.PostAsJsonAsync("/api/events/search", new
        {
            query = "  Strategy Night  ",
            sortBy = EventSortBy.Distance,
            page = 1,
            pageSize = 20,
            filters = new
            {
                category = EventCategory.Gaming,
                city = " Toronto ",
                tags = new[] { " Games ", "games", " Indoor " }
            },
            geo = new
            {
                lat = 43.6532,
                lng = -79.3832,
                radiusKm = 10
            }
        });
        search.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(search);
        body.Data!.Items.Select(item => item.Id).Should().ContainInOrder(nearer.Id, farther.Id);
        body.Data.Items.Select(item => item.DistanceKm).Should().ContainInOrder(0.8, 4.2);

        searchService.LastCriteria.Should().NotBeNull();
        searchService.LastCriteria!.Query.Should().Be("strategy night");
        searchService.LastCriteria.IsPrivate.Should().BeFalse();
        searchService.LastCriteria.LifecycleState.Should().Be(EventLifecycleState.Published);
        searchService.LastCriteria.Category.Should().Be(EventCategory.Gaming);
        searchService.LastCriteria.City.Should().Be("Toronto");
        searchService.LastCriteria.Tags.Should().Equal("games", "indoor");
        searchService.LastCriteria.SortBy.Should().Be(EventSortBy.Distance);
        searchService.LastCriteria.Lat.Should().Be(43.6532);
        searchService.LastCriteria.Lng.Should().Be(-79.3832);
        searchService.LastCriteria.RadiusKm.Should().Be(10);
    }

    [Fact]
    public async Task EventMutationEndpoints_ShouldRejectInvalidImagesAndCurrentVersionRollback()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-invalid-mutation-owner@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Invalid Mutation Club");
        var firstImage = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id);
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Invalid Mutation Event", firstImage.PublicUrl);

        var nonOwnedImage = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/images",
            organizerSession.AccessToken,
            JsonContent.Create(new { imageUrl = "https://example.com/not-owned.png" })));
        nonOwnedImage.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        for (var i = 0; i < 4; i++)
        {
            var pending = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id, ev.Id);
            var addImage = await app.Client.SendAsync(CreateAuthorizedRequest(
                HttpMethod.Post,
                $"/api/events/{ev.Id}/images",
                organizerSession.AccessToken,
                JsonContent.Create(new { imageUrl = pending.PublicUrl })));
            addImage.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var sixthImage = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id, ev.Id);
        var imageLimit = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/images",
            organizerSession.AccessToken,
            JsonContent.Create(new { imageUrl = sixthImage.PublicUrl })));
        imageLimit.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var rollbackCurrentVersion = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/versions/{ev.CurrentVersionNumber}/rollback",
            organizerSession.AccessToken,
            JsonContent.Create(new { })));
        rollbackCurrentVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EventLifecycleEndpoints_ShouldRejectInvalidTransitions_AndInvalidPublishedUpdates()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-lifecycle-invalid-owner@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Lifecycle Invalid Club");
        var image = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id);

        var createDraft = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/clubs/{club.Id}/drafts",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Transition Draft",
                description = "A draft event used to verify invalid lifecycle transitions.",
                location = "Studio 3",
                imageUrls = new[] { image.PublicUrl },
                isPrivate = false,
                maxParticipants = 20,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(4),
                endTime = DateTime.UtcNow.AddDays(4).AddHours(2),
                category = EventCategory.Other
            })));
        createDraft.StatusCode.Should().Be(HttpStatusCode.Created);
        var draft = (await app.ReadApiResponseAsync<ManagedEventResponse>(createDraft)).Data!;

        var cancelDraft = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{draft.Id}/cancel",
            organizerSession.AccessToken,
            JsonContent.Create(new { })));
        cancelDraft.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var archiveDraft = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{draft.Id}/archive",
            organizerSession.AccessToken,
            JsonContent.Create(new { })));
        archiveDraft.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var publish = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{draft.Id}/publish",
            organizerSession.AccessToken,
            JsonContent.Create(new { })));
        publish.StatusCode.Should().Be(HttpStatusCode.OK);
        var published = (await app.ReadApiResponseAsync<ManagedEventResponse>(publish)).Data!;

        var republish = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{published.Id}/publish",
            organizerSession.AccessToken,
            JsonContent.Create(new { })));
        republish.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var cancel = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{published.Id}/cancel",
            organizerSession.AccessToken,
            JsonContent.Create(new { })));
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateCancelled = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/events/{published.Id}",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Cancelled Update Attempt",
                description = "A cancelled event should reject standard updates.",
                location = "Elsewhere",
                imageUrls = published.ImageUrls,
                isPrivate = false,
                maxParticipants = 20,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(5),
                endTime = DateTime.UtcNow.AddDays(5).AddHours(2),
                category = EventCategory.Other,
                venueName = "Room B",
                city = "Toronto",
                tags = new[] { "cancelled" }
            })));
        updateCancelled.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var cancelAgain = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{published.Id}/cancel",
            organizerSession.AccessToken,
            JsonContent.Create(new { })));
        cancelAgain.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
        var diagnostics = await app.DescribeFailureAsync(response);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new Xunit.Sdk.XunitException(diagnostics);
        }
        await app.ReindexClubsAsync();

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
        bool isPrivate = false,
        EventCategory category = EventCategory.Other,
        string city = "Toronto",
        string venueName = "Room A",
        double? latitude = null,
        double? longitude = null,
        string[]? tags = null)
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
                category,
                venueName,
                city,
                latitude,
                longitude,
                tags = tags ?? ["testing"]
            })));
        var diagnostics = await app.DescribeFailureAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.Created, diagnostics);
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
        await app.ReindexEventsAsync();

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

    private static string GetImageUploadIntentKey(string imageUrl)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl));
        return $"event:image-upload:intent:{Convert.ToHexString(bytes)}";
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

        // Soft delete: the row persists with a Cancelled status rather than being removed.
        var persistedCancelled = await app.QueryDbAsync(db =>
            db.EventRegistrations.SingleOrDefaultAsync(r => r.EventId == ev.Id && r.UserId == participant!.Id));
        persistedCancelled.Should().NotBeNull();
        persistedCancelled!.Status.Should().Be(RegistrationStatus.Cancelled);
        persistedCancelled.CancelledAt.Should().NotBeNull();

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

        var persistedRegistration = await app.QueryDbAsync(db =>
            db.EventRegistrations.SingleOrDefaultAsync(r => r.EventId == ev.Id && r.UserId == participant!.Id));
        persistedRegistration.Should().NotBeNull();
        persistedRegistration!.Notes.Should().Be("Please seat me near the front.");
        persistedRegistration.PhoneNumber.Should().Be("416-555-0123");
        persistedRegistration.DietaryNeeds.Should().Be("Vegetarian, no nuts");
        persistedRegistration.Status.Should().Be(RegistrationStatus.Active);

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

        var persistedPatched = await app.QueryDbAsync(db =>
            db.EventRegistrations.SingleAsync(r => r.EventId == ev.Id && r.UserId == participant!.Id));
        persistedPatched.Notes.Should().Be("Updated note");
        persistedPatched.PhoneNumber.Should().Be("647-555-9999");
        persistedPatched.DietaryNeeds.Should().Be("Gluten free");

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

    [Fact]
    public async Task DraftWorkflowEndpoints_ShouldCreateUpdateAndPublishDrafts()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "draft-workflow-owner@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Draft Endpoint Club");
        var image = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id);

        var createDraft = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/clubs/{club.Id}/drafts",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Draft Summit",
                description = "Draft event created through the integration coverage flow.",
                location = "Studio 1",
                imageUrls = new[] { image.PublicUrl },
                isPrivate = false,
                maxParticipants = 25,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(5),
                endTime = DateTime.UtcNow.AddDays(5).AddHours(2),
                category = EventCategory.Other,
                venueName = "North Hall",
                city = "Toronto",
                tags = new[] { "draft", "workflow" }
            })));

        createDraft.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdDraft = (await app.ReadApiResponseAsync<ManagedEventResponse>(createDraft)).Data!;
        createdDraft.LifecycleState.Should().Be(EventLifecycleState.Draft);
        createdDraft.PublishReady.Should().BeTrue();

        var updateDraft = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"/api/events/{createdDraft.Id}/draft",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Draft Summit Updated",
                location = "Studio 2",
                maxParticipants = 40,
                tags = new[] { "draft", "updated" }
            })));

        updateDraft.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedDraft = (await app.ReadApiResponseAsync<ManagedEventResponse>(updateDraft)).Data!;
        updatedDraft.Name.Should().Be("Draft Summit Updated");
        updatedDraft.Location.Should().Be("Studio 2");
        updatedDraft.MaxParticipants.Should().Be(40);
        updatedDraft.Tags.Should().Contain(new[] { "draft", "updated" });

        var persistedDraft = await app.QueryDbAsync(db => db.Events.SingleAsync(e => e.Id == createdDraft.Id));
        persistedDraft.Name.Should().Be("Draft Summit Updated");
        persistedDraft.Location.Should().Be("Studio 2");
        persistedDraft.maxParticipants.Should().Be(40);
        persistedDraft.LifecycleState.Should().Be(EventLifecycleState.Draft);

        var publish = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{createdDraft.Id}/publish",
            organizerSession.AccessToken,
            JsonContent.Create(new { })));

        publish.StatusCode.Should().Be(HttpStatusCode.OK);
        var published = (await app.ReadApiResponseAsync<ManagedEventResponse>(publish)).Data!;
        published.LifecycleState.Should().Be(EventLifecycleState.Published);

        (await app.QueryDbAsync(db => db.Events.Where(e => e.Id == createdDraft.Id).Select(e => e.LifecycleState).SingleAsync()))
            .Should().Be(EventLifecycleState.Published);

        var publicDetail = await app.Client.GetAsync($"/api/events/{createdDraft.Id}");
        publicDetail.StatusCode.Should().Be(HttpStatusCode.OK);
        var publicBody = await app.ReadApiResponseAsync<EventResponse>(publicDetail);
        publicBody.Data!.Name.Should().Be("Draft Summit Updated");
    }

    [Fact]
    public async Task VersionLifecycleAndAnalyticsEndpoints_ShouldReturnHistoryRollbackAndMetrics()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, organizer) = await CreateUserSessionAsync(app, "version-analytics-owner@example.com", "Organizer");
        var (_, attendeeOne) = await CreateUserSessionAsync(app, "version-analytics-attendee1@example.com");
        var (_, attendeeTwo) = await CreateUserSessionAsync(app, "version-analytics-attendee2@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Versioning Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Versioned Event");

        var update = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/events/{ev.Id}",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Versioned Event Updated",
                description = "A detailed event description for integration testing coverage.",
                location = "Innovation Hub",
                imageUrls = ev.ImageUrls,
                isPrivate = false,
                maxParticipants = 50,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(7),
                endTime = DateTime.UtcNow.AddDays(7).AddHours(2),
                category = EventCategory.Other,
                venueName = "Room 202",
                city = "Toronto",
                tags = new[] { "versioned", "updated" }
            })));
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var persistedActionTypes = await app.QueryDbAsync(db =>
            db.EventVersions.Where(v => v.EventId == ev.Id).Select(v => v.ActionType).ToListAsync());
        persistedActionTypes.Should().Contain("create");
        persistedActionTypes.Should().Contain("update");

        await app.AddRegistrationAsync(ev.Id, attendeeOne!.Id);
        await app.AddRegistrationAsync(ev.Id, attendeeTwo!.Id);
        await app.AddPaymentAsync(ev.Id, attendeeOne.Id, PaymentStatus.Succeeded);
        await app.AddPaymentAsync(ev.Id, attendeeTwo.Id, PaymentStatus.Pending);

        var versions = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{ev.Id}/versions?page=0&pageSize=200",
            organizerSession.AccessToken));
        versions.StatusCode.Should().Be(HttpStatusCode.OK);
        var versionsBody = await app.ReadApiResponseAsync<PagedResponse<EventVersionListItemResponse>>(versions);
        versionsBody.Data!.Page.Should().Be(1);
        versionsBody.Data.PageSize.Should().Be(100);
        versionsBody.Data.Items.Should().HaveCountGreaterThanOrEqualTo(2);
        versionsBody.Data.Items.Should().Contain(item => item.ActionType == "create");
        versionsBody.Data.Items.Should().Contain(item => item.ActionType == "update");

        var versionDetail = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{ev.Id}/versions/1",
            organizerSession.AccessToken));
        versionDetail.StatusCode.Should().Be(HttpStatusCode.OK);
        var versionDetailBody = await app.ReadApiResponseAsync<EventVersionDetailResponse>(versionDetail);
        versionDetailBody.Data!.VersionNumber.Should().Be(1);
        versionDetailBody.Data.Snapshot.Name.Should().Be("Versioned Event");

        var rollback = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/versions/1/rollback",
            organizerSession.AccessToken,
            JsonContent.Create(new { })));
        rollback.StatusCode.Should().Be(HttpStatusCode.OK);
        var rollbackBody = await app.ReadApiResponseAsync<EventRollbackResponse>(rollback);
        rollbackBody.Data!.Event.Name.Should().Be("Versioned Event");
        rollbackBody.Data.RestoredFromVersionNumber.Should().Be(1);

        (await app.QueryDbAsync(db => db.Events.Where(e => e.Id == ev.Id).Select(e => e.Name).SingleAsync()))
            .Should().Be("Versioned Event");

        var eventAnalytics = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{ev.Id}/analytics",
            organizerSession.AccessToken));
        eventAnalytics.StatusCode.Should().Be(HttpStatusCode.OK);
        var eventAnalyticsBody = await app.ReadApiResponseAsync<EventAnalyticsResponse>(eventAnalytics);
        eventAnalyticsBody.Data!.EventId.Should().Be(ev.Id);
        eventAnalyticsBody.Data.RegistrationCount.Should().Be(2);
        eventAnalyticsBody.Data.TotalRevenue.Should().Be(1000);
        eventAnalyticsBody.Data.PendingRevenue.Should().Be(1000);
        eventAnalyticsBody.Data.SpotsRemaining.Should().BeGreaterThanOrEqualTo(0);

        var clubAnalytics = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/clubs/{club.Id}/analytics",
            organizerSession.AccessToken));
        clubAnalytics.StatusCode.Should().Be(HttpStatusCode.OK);
        var clubAnalyticsBody = await app.ReadApiResponseAsync<ClubAnalyticsResponse>(clubAnalytics);
        clubAnalyticsBody.Data!.ClubId.Should().Be(club.Id);
        clubAnalyticsBody.Data.TotalEvents.Should().BeGreaterThanOrEqualTo(1);
        clubAnalyticsBody.Data.TotalRegistrations.Should().Be(2);
        clubAnalyticsBody.Data.UniqueAttendees.Should().Be(2);
        clubAnalyticsBody.Data.TotalRevenue.Should().Be(1000);
        clubAnalyticsBody.Data.PendingRevenue.Should().Be(1000);
        clubAnalyticsBody.Data.TopEventsByRegistrations.Should().Contain(item => item.Id == ev.Id);

        var cancel = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/cancel",
            organizerSession.AccessToken,
            JsonContent.Create(new { })));
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = (await app.ReadApiResponseAsync<ManagedEventResponse>(cancel)).Data!;
        cancelled.LifecycleState.Should().Be(EventLifecycleState.Cancelled);

        (await app.QueryDbAsync(db => db.Events.Where(e => e.Id == ev.Id).Select(e => e.LifecycleState).SingleAsync()))
            .Should().Be(EventLifecycleState.Cancelled);

        organizer.Should().NotBeNull();
    }

    [Fact]
    public async Task EventVersioningEndpoints_ShouldReturnNotFound_ForUnknownVersions()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "version-missing-owner@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Missing Version Club");
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Missing Version Event");

        var versionDetail = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{ev.Id}/versions/999",
            organizerSession.AccessToken));
        versionDetail.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var rollback = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/versions/999/rollback",
            organizerSession.AccessToken,
            JsonContent.Create(new { })));
        rollback.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BatchAndListingEndpoints_ShouldCreateListUpdateAndDeleteEvents()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "batch-endpoints-owner@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Batch Events Club");
        var firstImage = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id);
        var secondImage = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id);

        var batchCreate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/batch/{club.Id}",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                events = new object[]
                {
                    new
                    {
                        name = "Batch Event One",
                        description = "The first published batch event for endpoint coverage.",
                        location = "Hall A",
                        imageUrls = new[] { firstImage.PublicUrl },
                        isPrivate = false,
                        maxParticipants = 20,
                        registerCost = 0,
                        startTime = DateTime.UtcNow.AddDays(9),
                        endTime = DateTime.UtcNow.AddDays(9).AddHours(2),
                        category = EventCategory.Other,
                        venueName = "Hall A",
                        city = "Toronto",
                        tags = new[] { "batch", "one" }
                    },
                    new
                    {
                        name = "Batch Event Two",
                        description = "The second published batch event for endpoint coverage.",
                        location = "Hall B",
                        imageUrls = new[] { secondImage.PublicUrl },
                        isPrivate = false,
                        maxParticipants = 30,
                        registerCost = 0,
                        startTime = DateTime.UtcNow.AddDays(10),
                        endTime = DateTime.UtcNow.AddDays(10).AddHours(2),
                        category = EventCategory.Other,
                        venueName = "Hall B",
                        city = "Toronto",
                        tags = new[] { "batch", "two" }
                    }
                }
            })));
        batchCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        var batchCreateBody = await app.ReadApiResponseAsync<BatchCreateResultResponse>(batchCreate);
        batchCreateBody.Data!.Created.Should().HaveCount(2);
        var createdIds = batchCreateBody.Data.Created.Select(item => item.Id).ToArray();

        (await app.QueryDbAsync(db => db.Events.CountAsync(e => createdIds.Contains(e.Id)))).Should().Be(2);

        await app.ReindexEventsAsync();

        var list = await app.Client.GetAsync("/api/events?search=Batch%20Event&page=1&pageSize=20");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(list);
        listBody.Data!.Items.Select(item => item.Id).Should().Contain(createdIds);

        var batchUpdate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            "/api/events/batch",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                events = new object[]
                {
                    new
                    {
                        eventId = createdIds[0],
                        name = "Batch Event One Updated",
                        city = "Ottawa"
                    },
                    new
                    {
                        eventId = createdIds[1],
                        tags = new[] { "batch", "refined" },
                        maxParticipants = 45
                    }
                }
            })));
        batchUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
        var batchUpdateBody = await app.ReadApiResponseAsync<BatchMutationCountResponse>(batchUpdate);
        batchUpdateBody.Data!.UpdatedCount.Should().Be(2);

        var updatedDetail = await app.Client.GetAsync($"/api/events/{createdIds[0]}");
        updatedDetail.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedDetailBody = await app.ReadApiResponseAsync<EventResponse>(updatedDetail);
        updatedDetailBody.Data!.Name.Should().Be("Batch Event One Updated");
        updatedDetailBody.Data.City.Should().Be("Ottawa");

        var persistedBatchUpdated = await app.QueryDbAsync(db => db.Events.SingleAsync(e => e.Id == createdIds[0]));
        persistedBatchUpdated.Name.Should().Be("Batch Event One Updated");
        persistedBatchUpdated.City.Should().Be("Ottawa");
        (await app.QueryDbAsync(db => db.Events.Where(e => e.Id == createdIds[1]).Select(e => e.maxParticipants).SingleAsync()))
            .Should().Be(45);

        var batchDelete = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            "/api/events/batch",
            organizerSession.AccessToken,
            JsonContent.Create(new { ids = createdIds })));
        batchDelete.StatusCode.Should().Be(HttpStatusCode.OK);
        var batchDeleteBody = await app.ReadApiResponseAsync<BatchDeleteCountResponse>(batchDelete);
        batchDeleteBody.Data!.DeletedCount.Should().Be(2);

        (await app.QueryDbAsync(db => db.Events.AnyAsync(e => createdIds.Contains(e.Id)))).Should().BeFalse();

        var missing = await app.Client.GetAsync($"/api/events/{createdIds[0]}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BatchEventMutationEndpoints_ShouldRejectInvalidPayloads_Drafts_AndMissingEvents()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "batch-negative-owner@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Batch Negative Club");
        var published = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Published Batch Event");

        var createDraft = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{club.Id}",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Draft Batch Event",
                description = "A draft event used to reject batch mutations cleanly.",
                location = "Studio Draft",
                imageUrls = new[] { (await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id)).PublicUrl },
                isPrivate = false,
                maxParticipants = 25,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(5),
                endTime = DateTime.UtcNow.AddDays(5).AddHours(2),
                category = EventCategory.Other,
                venueName = "Draft Room",
                city = "Toronto",
                tags = new[] { "draft", "batch" }
            })));
        createDraft.StatusCode.Should().Be(HttpStatusCode.Created);
        var draft = (await app.ReadApiResponseAsync<EventResponse>(createDraft)).Data!;

        var duplicateUpdate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            "/api/events/batch",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                events = new object[]
                {
                    new { eventId = published.Id, name = "Duplicate Update One" },
                    new { eventId = published.Id, name = "Duplicate Update Two" }
                }
            })));
        duplicateUpdate.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var draftBatchUpdate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            "/api/events/batch",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                events = new object[]
                {
                    new { eventId = draft.Id, name = "Draft Batch Update" }
                }
            })));
        draftBatchUpdate.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var duplicateDelete = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            "/api/events/batch",
            organizerSession.AccessToken,
            JsonContent.Create(new { ids = new[] { published.Id, published.Id } })));
        duplicateDelete.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var missingDelete = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            "/api/events/batch",
            organizerSession.AccessToken,
            JsonContent.Create(new { ids = new[] { published.Id, 999999 } })));
        missingDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var emptyBatchCreate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/batch/{club.Id}",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                events = Array.Empty<object>()
            })));
        emptyBatchCreate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EventManagementEndpoints_ShouldReturnForbidden_ForOutsiders()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-authz-owner@example.com", "Organizer");
        var (outsiderSession, _) = await CreateUserSessionAsync(app, "events-authz-outsider@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Authz Events Club");
        var creationImage = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id);

        var draftCreate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{club.Id}",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Protected Draft Event",
                description = "A draft event used to verify forbidden management responses.",
                location = "Student Center",
                imageUrls = new[] { creationImage.PublicUrl },
                isPrivate = false,
                maxParticipants = 25,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(7),
                endTime = DateTime.UtcNow.AddDays(7).AddHours(2),
                category = EventCategory.Other,
                venueName = "Room 10",
                city = "Toronto",
                tags = new[] { "authz", "draft" }
            })));
        draftCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        var draftEvent = (await app.ReadApiResponseAsync<EventResponse>(draftCreate)).Data!;

        var publishedEvent = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Protected Published Event");
        var secondaryImage = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id, publishedEvent.Id);

        var update = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/events/{publishedEvent.Id}",
            outsiderSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Outsider Update Attempt",
                description = "An outsider should not be able to update this protected event.",
                location = "Elsewhere",
                imageUrls = publishedEvent.ImageUrls,
                isPrivate = false,
                maxParticipants = 30,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(8),
                endTime = DateTime.UtcNow.AddDays(8).AddHours(2),
                category = EventCategory.Other,
                venueName = "Room 11",
                city = "Toronto",
                tags = new[] { "authz" }
            })));
        update.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var delete = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/events/{publishedEvent.Id}",
            outsiderSession.AccessToken));
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var publish = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{draftEvent.Id}/publish",
            outsiderSession.AccessToken,
            JsonContent.Create(new { })));
        publish.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cancel = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{publishedEvent.Id}/cancel",
            outsiderSession.AccessToken,
            JsonContent.Create(new { })));
        cancel.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var archive = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{publishedEvent.Id}/archive",
            outsiderSession.AccessToken,
            JsonContent.Create(new { })));
        archive.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var manageDetail = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{publishedEvent.Id}/manage",
            outsiderSession.AccessToken));
        manageDetail.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var manageList = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/clubs/{club.Id}/manage?page=1&pageSize=20",
            outsiderSession.AccessToken));
        manageList.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var versions = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{publishedEvent.Id}/versions",
            outsiderSession.AccessToken));
        versions.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var versionDetail = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{publishedEvent.Id}/versions/1",
            outsiderSession.AccessToken));
        versionDetail.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var rollback = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{publishedEvent.Id}/versions/1/rollback",
            outsiderSession.AccessToken,
            JsonContent.Create(new { })));
        rollback.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var analytics = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{publishedEvent.Id}/analytics",
            outsiderSession.AccessToken));
        analytics.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var clubAnalytics = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/clubs/{club.Id}/analytics",
            outsiderSession.AccessToken));
        clubAnalytics.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var imagePresign = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/images/presigned-url",
            outsiderSession.AccessToken,
            JsonContent.Create(new
            {
                clubId = club.Id,
                eventId = publishedEvent.Id,
                fileName = "outsider.png",
                contentType = "image/png"
            })));
        imagePresign.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var addImage = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{publishedEvent.Id}/images",
            outsiderSession.AccessToken,
            JsonContent.Create(new { imageUrl = secondaryImage.PublicUrl })));
        addImage.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var batchCreate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/batch/{club.Id}",
            outsiderSession.AccessToken,
            JsonContent.Create(new
            {
                events = new object[]
                {
                    new
                    {
                        name = "Forbidden Batch Event",
                        description = "An outsider should not be able to create batch events here.",
                        location = "Hall A",
                        imageUrls = new[] { creationImage.PublicUrl },
                        isPrivate = false,
                        maxParticipants = 20,
                        registerCost = 0,
                        startTime = DateTime.UtcNow.AddDays(9),
                        endTime = DateTime.UtcNow.AddDays(9).AddHours(2),
                        category = EventCategory.Other,
                        venueName = "Hall A",
                        city = "Toronto",
                        tags = new[] { "authz" }
                    }
                }
            })));
        batchCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var batchUpdate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            "/api/events/batch",
            outsiderSession.AccessToken,
            JsonContent.Create(new
            {
                events = new object[]
                {
                    new
                    {
                        eventId = publishedEvent.Id,
                        name = "Forbidden Batch Update"
                    }
                }
            })));
        batchUpdate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var batchDelete = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            "/api/events/batch",
            outsiderSession.AccessToken,
            JsonContent.Create(new { ids = new[] { publishedEvent.Id } })));
        batchDelete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task VolunteerEventMediaAccess_ShouldAllowImages_ButRejectBroaderEventManagement()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, organizer) = await CreateUserSessionAsync(app, "events-volunteer-owner@example.com", "Organizer");
        var (volunteerSession, volunteer) = await CreateUserSessionAsync(app, "events-volunteer@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Volunteer Media Club");
        await app.AddClubStaffAsync(club.Id, volunteer!.Id, organizer!.Id, ClubStaffRole.Volunteer);

        var initialImage = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id);
        var ev = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Volunteer Media Event", initialImage.PublicUrl);

        var clubLevelPresign = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/images/presigned-url",
            volunteerSession.AccessToken,
            JsonContent.Create(new
            {
                clubId = club.Id,
                fileName = "club-level.png",
                contentType = "image/png"
            })));
        clubLevelPresign.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var eventPresign = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/images/presigned-url",
            volunteerSession.AccessToken,
            JsonContent.Create(new
            {
                clubId = club.Id,
                eventId = ev.Id,
                fileName = "volunteer.png",
                contentType = "image/png"
            })));
        eventPresign.StatusCode.Should().Be(HttpStatusCode.OK);
        var presignBody = await app.ReadApiResponseAsync<PresignedUploadResponse>(eventPresign);

        var addImage = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/images",
            volunteerSession.AccessToken,
            JsonContent.Create(new { imageUrl = presignBody.Data!.PublicUrl })));
        addImage.StatusCode.Should().Be(HttpStatusCode.Created);
        var addedImage = (await app.ReadApiResponseAsync<EventImageApiModel>(addImage)).Data!;

        var removeImage = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/events/{ev.Id}/images/{addedImage.Id}",
            volunteerSession.AccessToken));
        removeImage.StatusCode.Should().Be(HttpStatusCode.OK);

        var update = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/events/{ev.Id}",
            volunteerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Volunteer Update Attempt",
                description = "Volunteers should not be able to update broader event details here.",
                location = "Elsewhere",
                imageUrls = ev.ImageUrls,
                isPrivate = false,
                maxParticipants = 30,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(8),
                endTime = DateTime.UtcNow.AddDays(8).AddHours(2),
                category = EventCategory.Other,
                venueName = "Room 12",
                city = "Toronto",
                tags = new[] { "volunteer" }
            })));
        update.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cancel = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/cancel",
            volunteerSession.AccessToken,
            JsonContent.Create(new { })));
        cancel.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProtectedEventEndpoints_ShouldReturnUnauthorized_WhenCallerIsUnauthenticated()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "events-unauth-owner@example.com", "Organizer");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Unauth Events Club");
        var creationImage = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id);

        var draftCreate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{club.Id}",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Unauth Draft Event",
                description = "A draft event used to verify unauthenticated event management responses.",
                location = "Student Center",
                imageUrls = new[] { creationImage.PublicUrl },
                isPrivate = false,
                maxParticipants = 25,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(7),
                endTime = DateTime.UtcNow.AddDays(7).AddHours(2),
                category = EventCategory.Other,
                venueName = "Room 13",
                city = "Toronto",
                tags = new[] { "authz", "unauthenticated" }
            })));
        draftCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        var draftEvent = (await app.ReadApiResponseAsync<EventResponse>(draftCreate)).Data!;

        var publishedEvent = await CreateEventAsync(app, organizerSession.AccessToken, club.Id, "Unauth Published Event");
        var eventImage = await CreatePendingImageAsync(app, organizerSession.AccessToken, club.Id, publishedEvent.Id);

        var create = await app.Client.PostAsJsonAsync($"/api/events/{club.Id}", new
        {
            name = "Anonymous Create Attempt",
            description = "Unauthenticated callers should not be able to create events.",
            location = "Elsewhere",
            imageUrls = new[] { creationImage.PublicUrl },
            isPrivate = false,
            maxParticipants = 25,
            registerCost = 0,
            startTime = DateTime.UtcNow.AddDays(10),
            endTime = DateTime.UtcNow.AddDays(10).AddHours(2),
            category = EventCategory.Other,
            venueName = "Room 14",
            city = "Toronto",
            tags = new[] { "unauthenticated" }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var update = await app.Client.PutAsJsonAsync($"/api/events/{publishedEvent.Id}", new
        {
            name = "Anonymous Update Attempt",
            description = "Unauthenticated callers should not be able to update events.",
            location = "Elsewhere",
            imageUrls = publishedEvent.ImageUrls,
            isPrivate = false,
            maxParticipants = 30,
            registerCost = 0,
            startTime = DateTime.UtcNow.AddDays(8),
            endTime = DateTime.UtcNow.AddDays(8).AddHours(2),
            category = EventCategory.Other,
            venueName = "Room 15",
            city = "Toronto",
            tags = new[] { "unauthenticated" }
        });
        update.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var delete = await app.Client.DeleteAsync($"/api/events/{publishedEvent.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var publish = await app.Client.PostAsJsonAsync($"/api/events/{draftEvent.Id}/publish", new { });
        publish.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var cancel = await app.Client.PostAsJsonAsync($"/api/events/{publishedEvent.Id}/cancel", new { });
        cancel.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var archive = await app.Client.PostAsJsonAsync($"/api/events/{publishedEvent.Id}/archive", new { });
        archive.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var manageDetail = await app.Client.GetAsync($"/api/events/{publishedEvent.Id}/manage");
        manageDetail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var manageList = await app.Client.GetAsync($"/api/events/clubs/{club.Id}/manage?page=1&pageSize=20");
        manageList.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var versions = await app.Client.GetAsync($"/api/events/{publishedEvent.Id}/versions");
        versions.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var versionDetail = await app.Client.GetAsync($"/api/events/{publishedEvent.Id}/versions/1");
        versionDetail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var rollback = await app.Client.PostAsJsonAsync($"/api/events/{publishedEvent.Id}/versions/1/rollback", new { });
        rollback.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var analytics = await app.Client.GetAsync($"/api/events/{publishedEvent.Id}/analytics");
        analytics.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var clubAnalytics = await app.Client.GetAsync($"/api/events/clubs/{club.Id}/analytics");
        clubAnalytics.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var imagePresign = await app.Client.PostAsJsonAsync("/api/events/images/presigned-url", new
        {
            clubId = club.Id,
            eventId = publishedEvent.Id,
            fileName = "anonymous.png",
            contentType = "image/png"
        });
        imagePresign.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var addImage = await app.Client.PostAsJsonAsync($"/api/events/{publishedEvent.Id}/images", new
        {
            imageUrl = eventImage.PublicUrl
        });
        addImage.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var register = await app.Client.PostAsJsonAsync($"/api/events/{publishedEvent.Id}/register", new
        {
            notes = "Anonymous register attempt"
        });
        register.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unregister = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/events/{publishedEvent.Id}/register"));
        unregister.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var updateRegistration = await app.Client.PatchAsync(
            $"/api/events/{publishedEvent.Id}/register",
            JsonContent.Create(new { notes = "Anonymous patch attempt" }));
        updateRegistration.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var myRegistration = await app.Client.GetAsync($"/api/events/{publishedEvent.Id}/registrations/me");
        myRegistration.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var batchRegister = await app.Client.PostAsJsonAsync("/api/events/batch/register", new
        {
            eventIds = new[] { publishedEvent.Id }
        });
        batchRegister.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var batchUnregister = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/events/batch/register")
        {
            Content = JsonContent.Create(new
            {
                eventIds = new[] { publishedEvent.Id }
            })
        });
        batchUnregister.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var batchCreate = await app.Client.PostAsJsonAsync($"/api/events/batch/{club.Id}", new
        {
            events = new object[]
            {
                new
                {
                    name = "Anonymous Batch Event",
                    description = "Unauthenticated callers should not be able to create batch events.",
                    location = "Hall A",
                    imageUrls = new[] { creationImage.PublicUrl },
                    isPrivate = false,
                    maxParticipants = 20,
                    registerCost = 0,
                    startTime = DateTime.UtcNow.AddDays(11),
                    endTime = DateTime.UtcNow.AddDays(11).AddHours(2),
                    category = EventCategory.Other,
                    venueName = "Hall A",
                    city = "Toronto",
                    tags = new[] { "unauthenticated" }
                }
            }
        });
        batchCreate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var batchUpdate = await app.Client.PutAsJsonAsync("/api/events/batch", new
        {
            events = new object[]
            {
                new
                {
                    eventId = publishedEvent.Id,
                    name = "Anonymous Batch Update"
                }
            }
        });
        batchUpdate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var batchDelete = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/events/batch")
        {
            Content = JsonContent.Create(new
            {
                ids = new[] { publishedEvent.Id }
            })
        });
        batchDelete.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

    private sealed class BatchMutationCountResponse
    {
        public int UpdatedCount { get; init; }
    }

    private sealed class BatchDeleteCountResponse
    {
        public int DeletedCount { get; init; }
    }

    private sealed class UnavailableEventSearchService : IEventSearchService
    {
        public Task EnsureIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task IndexAsync(EventDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(int eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BulkIndexAsync(IEnumerable<EventDocument> documents, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<EventSearchResult> SearchAsync(EventSearchCriteria criteria) =>
            throw new ElasticsearchUnavailableException("Search is unavailable for this test.");
    }

    private sealed class StubEventSearchService : IEventSearchService
    {
        public EventSearchCriteria? LastCriteria { get; private set; }
        public EventSearchResult Result { get; set; } = new([], 0);
        public Task EnsureIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task IndexAsync(EventDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(int eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BulkIndexAsync(IEnumerable<EventDocument> documents, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<EventSearchResult> SearchAsync(EventSearchCriteria criteria)
        {
            LastCriteria = criteria;
            return Task.FromResult(Result);
        }
    }
}
