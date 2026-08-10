using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.staff;
using backend.main.features.events;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.favourites.contracts.responses;
using backend.main.features.events.invitations;
using backend.main.features.events.invitations.contracts.responses;
using backend.main.features.events.registration;
using backend.main.features.events.registration.contracts.responses;
using backend.main.features.events.series.contracts.responses;
using backend.main.features.events.waitlist;
using backend.main.features.events.waitlist.contracts.responses;
using backend.main.shared.providers.messages;
using backend.main.shared.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Workflows;

[Trait("Category", "EndToEnd")]
public class EventOrganizationWorkflowTests
{
    [Fact]
    public async Task OrganizerToAttendee_ShouldCreatePublishDiscoverRegisterAndReportAttendance()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserAsync(app, "workflow-organizer@example.com", "Organizer");
        var attendee = await CreateUserAsync(app, "workflow-attendee@example.com");

        var club = await CreateClubAsync(
            app,
            organizer.Session.AccessToken,
            "Workflow Community Club",
            "workflow-community@example.com");
        club.OwnerId.Should().Be(organizer.UserId);
        club.IsOwner.Should().BeTrue();

        var draft = await CreateDraftEventAsync(
            app,
            organizer.Session.AccessToken,
            club.Id,
            "Community Launch Night");
        draft.LifecycleState.Should().Be(EventLifecycleState.Draft);

        var beforePublish = await GetPublicClubEventsAsync(app, club.Id);
        beforePublish.Should().NotContain(item => item.Id == draft.Id);

        var published = await PublishEventAsync(app, organizer.Session.AccessToken, draft.Id);
        published.LifecycleState.Should().Be(EventLifecycleState.Published);

        var discovered = await GetPublicClubEventsAsync(app, club.Id);
        discovered.Should().ContainSingle(item =>
            item.Id == published.Id &&
            item.Name == "Community Launch Night" &&
            item.RegistrationCount == 0);

        var registration = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{published.Id}/register",
            attendee.Session.AccessToken,
            JsonContent.Create(new
            {
                notes = "First-time attendee",
                phoneNumber = "+14165550101",
                dietaryNeeds = "Vegetarian"
            })));
        registration.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(registration));

        var attendeeRegistrations = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/users/{attendee.UserId}/events/registered",
            attendee.Session.AccessToken));
        attendeeRegistrations.StatusCode.Should().Be(HttpStatusCode.OK);
        var attendeeRegistrationBody = await app.ReadApiResponseAsync<IEnumerable<EventRegistrationResponse>>(
            attendeeRegistrations);
        attendeeRegistrationBody.Data.Should().ContainSingle(item =>
            item.EventId == published.Id &&
            item.Status == RegistrationStatus.Active.ToString() &&
            item.DietaryNeeds == "Vegetarian");

        var organizerRegistrations = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{published.Id}/registrations",
            organizer.Session.AccessToken));
        organizerRegistrations.StatusCode.Should().Be(HttpStatusCode.OK);
        var organizerRegistrationBody = await app.ReadApiResponseAsync<IEnumerable<EventRegistrationResponse>>(
            organizerRegistrations);
        organizerRegistrationBody.Data.Should().ContainSingle(item =>
            item.UserId == attendee.UserId &&
            item.Notes == "First-time attendee" &&
            item.PhoneNumber == "+14165550101" &&
            item.DietaryNeeds == "Vegetarian");

        var analytics = await GetEventAnalyticsAsync(app, published.Id, organizer.Session.AccessToken);
        analytics.RegistrationCount.Should().Be(1);
        analytics.SpotsRemaining.Should().Be(EventCapacity - 1);

        var storedEvent = await app.QueryDbAsync(db => db.Events
            .AsNoTracking()
            .SingleAsync(item => item.Id == published.Id));
        storedEvent.LifecycleState.Should().Be(EventLifecycleState.Published);
        storedEvent.RegistrationCount.Should().Be(1);

        var storedRegistration = await app.QueryDbAsync(db => db.EventRegistrations
            .AsNoTracking()
            .SingleAsync(item => item.EventId == published.Id && item.UserId == attendee.UserId));
        storedRegistration.Status.Should().Be(RegistrationStatus.Active);
    }

    [Fact]
    public async Task DelegatedStaff_ShouldManagePublishAndObserveAttendeeRegistration()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateUserAsync(app, "workflow-owner@example.com", "Organizer");
        var manager = await CreateUserAsync(app, "workflow-manager@example.com");
        var attendee = await CreateUserAsync(app, "workflow-delegated-attendee@example.com");

        var club = await CreateClubAsync(
            app,
            owner.Session.AccessToken,
            "Delegated Events Club",
            "workflow-delegated@example.com");

        var addManager = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/staff/managers",
            owner.Session.AccessToken,
            JsonContent.Create(new { userId = manager.UserId })));
        addManager.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(addManager));

        var managedClubs = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            "/api/clubs/managed",
            manager.Session.AccessToken));
        managedClubs.StatusCode.Should().Be(HttpStatusCode.OK);
        var managedClubsBody = await app.ReadApiResponseAsync<IEnumerable<WorkflowClubResponse>>(managedClubs);
        managedClubsBody.Data.Should().ContainSingle(item =>
            item.Id == club.Id && item.IsManager && item.CanManage && !item.IsOwner);

        var draft = await CreateDraftEventAsync(
            app,
            manager.Session.AccessToken,
            club.Id,
            "Manager-Led Workshop");
        var published = await PublishEventAsync(app, manager.Session.AccessToken, draft.Id);

        var discovered = await GetPublicClubEventsAsync(app, club.Id);
        discovered.Should().ContainSingle(item => item.Id == published.Id);

        var registration = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{published.Id}/register",
            attendee.Session.AccessToken,
            JsonContent.Create(new
            {
                notes = "Requires accessible seating",
                phoneNumber = "+14165550102"
            })));
        registration.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(registration));

        var manageableEvent = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{published.Id}/manage",
            manager.Session.AccessToken));
        manageableEvent.StatusCode.Should().Be(HttpStatusCode.OK);
        var manageableEventBody = await app.ReadApiResponseAsync<ManagedEventResponse>(manageableEvent);
        manageableEventBody.Data.Should().Match<ManagedEventResponse>(item =>
            item.Id == published.Id &&
            item.ClubId == club.Id &&
            item.LifecycleState == EventLifecycleState.Published &&
            item.RegistrationCount == 1);

        var registrations = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{published.Id}/registrations",
            manager.Session.AccessToken));
        registrations.StatusCode.Should().Be(HttpStatusCode.OK);
        var registrationsBody = await app.ReadApiResponseAsync<IEnumerable<EventRegistrationResponse>>(registrations);
        registrationsBody.Data.Should().ContainSingle(item =>
            item.UserId == attendee.UserId &&
            item.Notes == "Requires accessible seating" &&
            item.PhoneNumber == "+14165550102");

        var analytics = await GetEventAnalyticsAsync(app, published.Id, manager.Session.AccessToken);
        analytics.RegistrationCount.Should().Be(1);

        var storedStaff = await app.QueryDbAsync(db => db.ClubStaff
            .AsNoTracking()
            .SingleAsync(item => item.ClubId == club.Id && item.UserId == manager.UserId));
        storedStaff.Role.Should().Be(ClubStaffRole.Manager);

        var storedEvent = await app.QueryDbAsync(db => db.Events
            .AsNoTracking()
            .SingleAsync(item => item.Id == published.Id));
        storedEvent.LifecycleState.Should().Be(EventLifecycleState.Published);
        storedEvent.RegistrationCount.Should().Be(1);

        var storedRegistration = await app.QueryDbAsync(db => db.EventRegistrations
            .AsNoTracking()
            .SingleAsync(item => item.EventId == published.Id && item.UserId == attendee.UserId));
        storedRegistration.Status.Should().Be(RegistrationStatus.Active);
    }

    [Fact]
    public async Task FullEventWaitlist_ShouldPromoteNextAttendeeWhenASeatIsReleased()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserAsync(app, "workflow-waitlist-organizer@example.com", "Organizer");
        var occupant = await CreateUserAsync(app, "workflow-waitlist-occupant@example.com");
        var waiting = await CreateUserAsync(app, "workflow-waitlist-next@example.com");

        var club = await CreateClubAsync(
            app,
            organizer.Session.AccessToken,
            "Workflow Waitlist Club",
            "workflow-waitlist@example.com");
        var draft = await CreateDraftEventAsync(
            app,
            organizer.Session.AccessToken,
            club.Id,
            "One-Seat Community Dinner",
            capacity: 1,
            waitlistEnabled: true);
        var published = await PublishEventAsync(app, organizer.Session.AccessToken, draft.Id);
        published.WaitlistEnabled.Should().BeTrue();
        published.MaxParticipants.Should().Be(1);

        var occupantRegistration = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{published.Id}/register",
            occupant.Session.AccessToken,
            JsonContent.Create(new { dietaryNeeds = "None" })));
        occupantRegistration.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await app.DescribeFailureAsync(occupantRegistration));

        var joinWaitlist = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{published.Id}/waitlist",
            waiting.Session.AccessToken,
            JsonContent.Create(new
            {
                notes = "Please notify me if a seat opens",
                phoneNumber = "+14165550103",
                dietaryNeeds = "Gluten-free"
            })));
        joinWaitlist.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(joinWaitlist));
        var joinedEntry = (await app.ReadApiResponseAsync<EventWaitlistEntryResponse>(joinWaitlist)).Data!;
        joinedEntry.Position.Should().Be(1);
        joinedEntry.Status.Should().Be(EventWaitlistEntryStatus.Waiting.ToString());

        var myWaitlists = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            "/api/events/me/waitlisted",
            waiting.Session.AccessToken));
        myWaitlists.StatusCode.Should().Be(HttpStatusCode.OK);
        var myWaitlistsBody = await app.ReadApiResponseAsync<IEnumerable<WaitlistedEventResponse>>(myWaitlists);
        myWaitlistsBody.Data.Should().ContainSingle(item =>
            item.Event.Id == published.Id && item.Position == 1 && !item.AccessRevoked);

        var organizerRoster = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{published.Id}/waitlist",
            organizer.Session.AccessToken));
        organizerRoster.StatusCode.Should().Be(HttpStatusCode.OK);
        var organizerRosterBody = await app.ReadApiResponseAsync<IEnumerable<EventWaitlistEntryResponse>>(
            organizerRoster);
        organizerRosterBody.Data.Should().ContainSingle(item =>
            item.UserId == waiting.UserId &&
            item.Position == 1 &&
            item.Notes == "Please notify me if a seat opens" &&
            item.PhoneNumber == "+14165550103" &&
            item.DietaryNeeds == "Gluten-free");

        app.Publisher.Clear();

        var unregister = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Delete,
            $"/api/events/{published.Id}/register",
            occupant.Session.AccessToken));
        unregister.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(unregister));

        var promotedRegistrations = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/users/{waiting.UserId}/events/registered",
            waiting.Session.AccessToken));
        promotedRegistrations.StatusCode.Should().Be(HttpStatusCode.OK);
        var promotedRegistrationsBody = await app.ReadApiResponseAsync<IEnumerable<EventRegistrationResponse>>(
            promotedRegistrations);
        promotedRegistrationsBody.Data.Should().ContainSingle(item =>
            item.EventId == published.Id &&
            item.Status == RegistrationStatus.Active.ToString() &&
            item.Notes == "Please notify me if a seat opens" &&
            item.PhoneNumber == "+14165550103" &&
            item.DietaryNeeds == "Gluten-free");

        var waitlistStatus = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{published.Id}/waitlist/me",
            waiting.Session.AccessToken));
        waitlistStatus.StatusCode.Should().Be(HttpStatusCode.OK);
        var waitlistStatusBody = await app.ReadApiResponseAsync<MyWaitlistStatusResponse>(waitlistStatus);
        waitlistStatusBody.Data!.OnWaitlist.Should().BeFalse();
        waitlistStatusBody.Data.Position.Should().BeNull();
        waitlistStatusBody.Data.WaitlistCount.Should().Be(0);

        var eventDetail = await app.Client.GetAsync($"/api/events/{published.Id}");
        eventDetail.StatusCode.Should().Be(HttpStatusCode.OK);
        var eventDetailBody = await app.ReadApiResponseAsync<EventResponse>(eventDetail);
        eventDetailBody.Data.Should().Match<EventResponse>(item =>
            item.RegistrationCount == 1 && item.WaitlistCount == 0 && item.WaitlistEnabled);

        var storedEntries = await app.QueryDbAsync(db => db.EventWaitlistEntries
            .AsNoTracking()
            .Where(item => item.EventId == published.Id)
            .ToListAsync());
        storedEntries.Should().ContainSingle(item =>
            item.UserId == waiting.UserId && item.Status == EventWaitlistEntryStatus.Promoted);

        var storedRegistrations = await app.QueryDbAsync(db => db.EventRegistrations
            .AsNoTracking()
            .Where(item => item.EventId == published.Id)
            .ToListAsync());
        storedRegistrations.Should().ContainSingle(item =>
            item.UserId == occupant.UserId && item.Status == RegistrationStatus.Cancelled);
        storedRegistrations.Should().ContainSingle(item =>
            item.UserId == waiting.UserId && item.Status == RegistrationStatus.Active);

        var storedEvent = await app.QueryDbAsync(db => db.Events
            .AsNoTracking()
            .SingleAsync(item => item.Id == published.Id));
        storedEvent.RegistrationCount.Should().Be(1);
        storedEvent.WaitlistCount.Should().Be(0);

        var promotionEmail = app.Publisher.EmailMessages.Should()
            .ContainSingle(message => message.Type == EmailMessageType.WaitlistPromoted)
            .Subject;
        promotionEmail.Email.Should().Be("workflow-waitlist-next@example.com");
        promotionEmail.EventId.Should().Be(published.Id);
    }

    [Fact]
    public async Task PrivateEventInvitation_ShouldGrantAccessAndAllowRegistrationOnlyForTheInvitee()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserAsync(app, "workflow-private-organizer@example.com", "Organizer");
        var invitee = await CreateUserAsync(app, "workflow-private-invitee@example.com");
        var outsider = await CreateUserAsync(app, "workflow-private-outsider@example.com");

        var club = await CreateClubAsync(
            app,
            organizer.Session.AccessToken,
            "Private Workflow Club",
            "workflow-private@example.com");
        var draft = await CreateDraftEventAsync(
            app,
            organizer.Session.AccessToken,
            club.Id,
            "Invite-Only Planning Session",
            isPrivate: true);
        var published = await PublishEventAsync(app, organizer.Session.AccessToken, draft.Id);
        published.IsPrivate.Should().BeTrue();

        var outsiderBeforeInvite = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{published.Id}",
            outsider.Session.AccessToken));
        outsiderBeforeInvite.StatusCode.Should().Be(HttpStatusCode.NotFound);

        app.Publisher.Clear();

        var createInvitation = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{published.Id}/invitations",
            organizer.Session.AccessToken,
            JsonContent.Create(new
            {
                userIds = new[] { invitee.UserId },
                expiresAt = DateTime.UtcNow.AddDays(5)
            })));
        createInvitation.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await app.DescribeFailureAsync(createInvitation));
        var invitation = (await app.ReadApiResponseAsync<IEnumerable<EventInvitationResponse>>(createInvitation))
            .Data!
            .Should()
            .ContainSingle(item => item.RecipientUserId == invitee.UserId)
            .Subject;
        invitation.EffectiveStatus.Should().Be("Pending");

        var myInvitations = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            "/api/events/me/invited",
            invitee.Session.AccessToken));
        myInvitations.StatusCode.Should().Be(HttpStatusCode.OK);
        var myInvitationsBody = await app.ReadApiResponseAsync<IEnumerable<EventInvitationResponse>>(myInvitations);
        myInvitationsBody.Data.Should().ContainSingle(item =>
            item.Id == invitation.Id && item.EventId == published.Id && item.EffectiveStatus == "Pending");

        var acceptInvitation = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/invitations/{invitation.Id}/accept",
            invitee.Session.AccessToken));
        acceptInvitation.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(acceptInvitation));
        var accepted = (await app.ReadApiResponseAsync<EventInvitationDecisionResponse>(acceptInvitation)).Data!;
        accepted.Invitation.EffectiveStatus.Should().Be("Accepted");

        var inviteeDetail = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{published.Id}",
            invitee.Session.AccessToken));
        inviteeDetail.StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.ReadApiResponseAsync<EventResponse>(inviteeDetail)).Data!.Id.Should().Be(published.Id);

        var inviteeRegistration = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{published.Id}/register",
            invitee.Session.AccessToken));
        inviteeRegistration.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await app.DescribeFailureAsync(inviteeRegistration));

        var outsiderAfterAcceptance = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{published.Id}",
            outsider.Session.AccessToken));
        outsiderAfterAcceptance.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var storedInvitation = await app.QueryDbAsync(db => db.EventInvitations
            .AsNoTracking()
            .SingleAsync(item => item.Id == invitation.Id));
        storedInvitation.LifecycleStatus.Should().Be(EventInvitationLifecycleStatus.Accepted);

        var storedRegistration = await app.QueryDbAsync(db => db.EventRegistrations
            .AsNoTracking()
            .SingleAsync(item => item.EventId == published.Id && item.UserId == invitee.UserId));
        storedRegistration.Status.Should().Be(RegistrationStatus.Active);

        app.Publisher.EmailMessages.Should().ContainSingle(message =>
            message.Type == EmailMessageType.EventInvite &&
            message.Email == "workflow-private-invitee@example.com" &&
            message.EventId == published.Id);
    }

    [Fact]
    public async Task FavouriteToRegistration_ShouldKeepTheEventPinnedAsCommitmentChanges()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserAsync(app, "workflow-pinned-organizer@example.com", "Organizer");
        var attendee = await CreateUserAsync(app, "workflow-pinned-attendee@example.com");

        var club = await CreateClubAsync(
            app,
            organizer.Session.AccessToken,
            "Pinned Journey Club",
            "workflow-pinned@example.com");
        var draft = await CreateDraftEventAsync(
            app,
            organizer.Session.AccessToken,
            club.Id,
            "Saved Then Joined Workshop");
        var published = await PublishEventAsync(app, organizer.Session.AccessToken, draft.Id);

        var favourite = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{published.Id}/favourite",
            attendee.Session.AccessToken));
        favourite.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(favourite));

        var pinnedBeforeRegistration = await GetPinnedEventsAsync(app, attendee.Session.AccessToken);
        pinnedBeforeRegistration.Should().ContainSingle(item =>
            item.Event.Id == published.Id && item.IsFavourited && !item.IsRegistered);

        var registration = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{published.Id}/register",
            attendee.Session.AccessToken));
        registration.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(registration));

        var pinnedAfterRegistration = await GetPinnedEventsAsync(app, attendee.Session.AccessToken);
        pinnedAfterRegistration.Should().ContainSingle(item =>
            item.Event.Id == published.Id && item.IsFavourited && item.IsRegistered);

        var unfavourite = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Delete,
            $"/api/events/{published.Id}/favourite",
            attendee.Session.AccessToken));
        unfavourite.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(unfavourite));

        var pinnedAfterUnfavourite = await GetPinnedEventsAsync(app, attendee.Session.AccessToken);
        pinnedAfterUnfavourite.Should().ContainSingle(item =>
            item.Event.Id == published.Id && !item.IsFavourited && item.IsRegistered);

        (await app.QueryDbAsync(db => db.EventFavourites.AnyAsync(item =>
            item.EventId == published.Id && item.UserId == attendee.UserId))).Should().BeFalse();
        (await app.QueryDbAsync(db => db.EventRegistrations.AnyAsync(item =>
            item.EventId == published.Id &&
            item.UserId == attendee.UserId &&
            item.Status == RegistrationStatus.Active))).Should().BeTrue();
    }

    [Fact]
    public async Task RecurringEventUpdate_ShouldRetainAndReportRegistrationsOnRetimedOccurrences()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var organizer = await CreateUserAsync(app, "workflow-series-organizer@example.com", "Organizer");
        var attendee = await CreateUserAsync(app, "workflow-series-attendee@example.com");

        var club = await CreateClubAsync(
            app,
            organizer.Session.AccessToken,
            "Recurring Workflow Club",
            "workflow-series@example.com");
        var draft = await CreateDraftEventAsync(
            app,
            organizer.Session.AccessToken,
            club.Id,
            "Weekly Community Workshop");

        var localStart = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd'T'19:00");
        var createSeries = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{draft.Id}/series",
            organizer.Session.AccessToken,
            JsonContent.Create(new
            {
                recurrence = new
                {
                    frequency = "Weekly",
                    interval = 1,
                    startLocalDateTime = localStart,
                    durationMinutes = 120,
                    timeZoneId = "America/Toronto",
                    endMode = "Count",
                    occurrenceCount = 3
                }
            })));
        createSeries.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(createSeries));
        var series = (await app.ReadApiResponseAsync<EventSeriesResponse>(createSeries)).Data!;
        series.Occurrences.Should().HaveCount(3);

        var publishSeries = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/series/{series.Id}/publish",
            organizer.Session.AccessToken));
        publishSeries.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(publishSeries));

        var registeredOccurrence = series.Occurrences[1];
        var originalStart = registeredOccurrence.StartTime;
        var registration = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{registeredOccurrence.Id}/register",
            attendee.Session.AccessToken,
            JsonContent.Create(new { notes = "Registered before the schedule change" })));
        registration.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(registration));

        var retime = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Patch,
            $"/api/events/series/{series.Id}/occurrences",
            organizer.Session.AccessToken,
            JsonContent.Create(new
            {
                fromEventId = registeredOccurrence.Id,
                localStartTime = "20:00"
            })));
        retime.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(retime));
        var retimeResult = (await app.ReadApiResponseAsync<EventSeriesBulkResultResponse>(retime)).Data!;
        retimeResult.AffectedEventIds.Should().Contain(registeredOccurrence.Id);
        retimeResult.RetimedWithRegistrations.Should().ContainSingle(id => id == registeredOccurrence.Id);
        retimeResult.Skipped.Should().BeEmpty();

        var attendeeRegistrations = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/users/{attendee.UserId}/events/registered",
            attendee.Session.AccessToken));
        attendeeRegistrations.StatusCode.Should().Be(HttpStatusCode.OK);
        var attendeeRegistrationsBody = await app.ReadApiResponseAsync<IEnumerable<EventRegistrationResponse>>(
            attendeeRegistrations);
        attendeeRegistrationsBody.Data.Should().ContainSingle(item =>
            item.EventId == registeredOccurrence.Id &&
            item.Status == RegistrationStatus.Active.ToString() &&
            item.Notes == "Registered before the schedule change");

        var storedOccurrence = await app.QueryDbAsync(db => db.Events
            .AsNoTracking()
            .SingleAsync(item => item.Id == registeredOccurrence.Id));
        storedOccurrence.StartTime.Should().NotBe(originalStart);
        storedOccurrence.RegistrationCount.Should().Be(1);
        storedOccurrence.SeriesId.Should().Be(series.Id);

        var storedRegistration = await app.QueryDbAsync(db => db.EventRegistrations
            .AsNoTracking()
            .SingleAsync(item =>
                item.EventId == registeredOccurrence.Id && item.UserId == attendee.UserId));
        storedRegistration.Status.Should().Be(RegistrationStatus.Active);
    }

    private const int EventCapacity = 24;

    private static async Task<WorkflowUser> CreateUserAsync(
        AuthApiTestApp app,
        string email,
        string role = "Participant")
    {
        var session = await app.SignUpAndVerifyByTokenAsync(
            email,
            role: role,
            transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync(email);
        user.Should().NotBeNull();
        return new WorkflowUser(session, user!.Id);
    }

    private static async Task<WorkflowClubResponse> CreateClubAsync(
        AuthApiTestApp app,
        string accessToken,
        string name,
        string email)
    {
        var response = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs",
            accessToken,
            JsonContent.Create(new
            {
                name,
                description = "End-to-end workflow club",
                clubtype = "social",
                clubImageUrl = app.BlobStorage.CreateOwnedBlobUrl("clubs", "workflow-club.png"),
                email
            })));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<WorkflowClubResponse>(response)).Data!;
    }

    private static async Task<ManagedEventResponse> CreateDraftEventAsync(
        AuthApiTestApp app,
        string accessToken,
        int clubId,
        string name,
        int capacity = EventCapacity,
        bool waitlistEnabled = false,
        bool isPrivate = false)
    {
        var imageResponse = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            "/api/events/images/presigned-url",
            accessToken,
            JsonContent.Create(new
            {
                clubId,
                fileName = "workflow-event.png",
                contentType = "image/png"
            })));
        imageResponse.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(imageResponse));
        var image = (await app.ReadApiResponseAsync<PresignedUploadResponse>(imageResponse)).Data!;

        var startTime = DateTime.UtcNow.AddDays(14);
        var draftResponse = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/clubs/{clubId}/drafts",
            accessToken,
            JsonContent.Create(new
            {
                name,
                description = "A complete event created through the end-to-end organization workflow.",
                location = "Community Centre",
                imageUrls = new[] { image.PublicUrl },
                isPrivate,
                maxParticipants = capacity,
                registerCost = 0,
                waitlistEnabled,
                startTime,
                endTime = startTime.AddHours(2),
                category = EventCategory.Social,
                venueName = "Main Hall",
                city = "Toronto",
                tags = new[] { "workflow", "community" }
            })));
        draftResponse.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(draftResponse));
        return (await app.ReadApiResponseAsync<ManagedEventResponse>(draftResponse)).Data!;
    }

    private static async Task<ManagedEventResponse> PublishEventAsync(
        AuthApiTestApp app,
        string accessToken,
        int eventId)
    {
        var response = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{eventId}/publish",
            accessToken,
            JsonContent.Create(new { })));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<ManagedEventResponse>(response)).Data!;
    }

    private static async Task<IReadOnlyCollection<EventResponse>> GetPublicClubEventsAsync(
        AuthApiTestApp app,
        int clubId)
    {
        var response = await app.Client.GetAsync($"/api/events/clubs/{clubId}?page=1&pageSize=20");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        var body = await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(response);
        return body.Data!.Items.ToArray();
    }

    private static async Task<EventAnalyticsResponse> GetEventAnalyticsAsync(
        AuthApiTestApp app,
        int eventId,
        string accessToken)
    {
        var response = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{eventId}/analytics",
            accessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<EventAnalyticsResponse>(response)).Data!;
    }

    private static async Task<IReadOnlyCollection<PinnedEventResponse>> GetPinnedEventsAsync(
        AuthApiTestApp app,
        string accessToken)
    {
        var response = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            "/api/events/me/pinned",
            accessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<IEnumerable<PinnedEventResponse>>(response)).Data!.ToArray();
    }

    private static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string path,
        string accessToken,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private sealed record WorkflowUser(AuthenticatedSessionResponse Session, int UserId);

    private sealed class WorkflowClubResponse
    {
        public int Id { get; init; }
        public int OwnerId { get; init; }
        public bool IsOwner { get; init; }
        public bool IsManager { get; init; }
        public bool CanManage { get; init; }
    }
}
