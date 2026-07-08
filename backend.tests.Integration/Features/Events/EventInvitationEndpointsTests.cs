using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.events;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.invitations;
using backend.main.features.events.invitations.contracts.responses;
using backend.main.infrastructure.database.core;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Events;

public class EventInvitationEndpointsTests
{
    [Fact]
    public async Task InvitationManagementEndpoints_ShouldCreateListRevoke_AndExposeMyInvitations()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "invite-owner@example.com", "Organizer");
        var (inviteeSession, invitee) = await CreateUserSessionAsync(app, "invite-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Invitation Management Club");
        var ev = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Private Invitation Event",
            isPrivate: true);

        app.Publisher.Clear();

        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/invitations",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                userIds = new[] { invitee!.Id },
                emails = new[] { "guest-invite@example.com" },
                expiresAt = DateTime.UtcNow.AddDays(3)
            })));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await app.ReadApiResponseAsync<IEnumerable<EventInvitationResponse>>(created);
        createdBody.Data.Should().HaveCount(2);
        createdBody.Data.Should().Contain(item => item.RecipientUserId == invitee.Id);
        createdBody.Data.Should().Contain(item => item.RecipientEmail == "guest-invite@example.com");

        (await app.QueryDbAsync(db => db.EventInvitations.CountAsync(i => i.EventId == ev.Id))).Should().Be(2);
        (await app.QueryDbAsync(db => db.EventInvitations.AnyAsync(i =>
            i.EventId == ev.Id &&
            i.RecipientUserId == invitee.Id &&
            i.LifecycleStatus == EventInvitationLifecycleStatus.Pending))).Should().BeTrue();

        app.Publisher.EmailMessages.Should().HaveCount(2);
        app.Publisher.EmailMessages.Should().OnlyContain(message =>
            message.Type == backend.main.shared.providers.messages.EmailMessageType.EventInvite);

        var invitations = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{ev.Id}/invitations",
            organizerSession.AccessToken));
        invitations.StatusCode.Should().Be(HttpStatusCode.OK);
        var invitationsBody = await app.ReadApiResponseAsync<IEnumerable<EventInvitationResponse>>(invitations);
        invitationsBody.Data.Should().HaveCount(2);

        var myInvitations = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/events/me/invited",
            inviteeSession.AccessToken));
        myInvitations.StatusCode.Should().Be(HttpStatusCode.OK);
        var myInvitationsBody = await app.ReadApiResponseAsync<IEnumerable<EventInvitationResponse>>(myInvitations);
        myInvitationsBody.Data.Should().ContainSingle(item =>
            item.EventId == ev.Id &&
            item.RecipientUserId == invitee.Id &&
            item.EffectiveStatus == "Pending");

        var emailInvitation = createdBody.Data.Single(item => item.RecipientEmail == "guest-invite@example.com");
        var revoked = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/invitations/{emailInvitation.Id}/revoke",
            organizerSession.AccessToken));
        revoked.StatusCode.Should().Be(HttpStatusCode.OK);
        var revokedBody = await app.ReadApiResponseAsync<EventInvitationResponse>(revoked);
        revokedBody.Data!.LifecycleStatus.Should().Be("Revoked");
        revokedBody.Data.EffectiveStatus.Should().Be("Revoked");

        var persistedRevoked = await app.QueryDbAsync(db =>
            db.EventInvitations.SingleAsync(i => i.Id == emailInvitation.Id));
        persistedRevoked.LifecycleStatus.Should().Be(EventInvitationLifecycleStatus.Revoked);
        persistedRevoked.RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task InvitationLinkEndpoints_ShouldCreateListResolveAccept_AndRevoke()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "link-owner@example.com", "Organizer");
        var (inviteeSession, _) = await CreateUserSessionAsync(app, "link-user@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Invitation Links Club");
        var ev = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Private Link Event",
            isPrivate: true);

        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/invitation-links",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                maxRedemptions = 2,
                expiresAt = DateTime.UtcNow.AddDays(5)
            })));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await app.ReadApiResponseAsync<EventInvitationLinkResponse>(created);
        createdBody.Data.Should().NotBeNull();
        createdBody.Data!.ShareUrl.Should().NotBeNullOrWhiteSpace();

        (await app.QueryDbAsync(db =>
            db.EventInvitationLinks.AnyAsync(l => l.Id == createdBody.Data.Id && l.EventId == ev.Id)))
            .Should().BeTrue();

        var token = ExtractToken(createdBody.Data.ShareUrl!);

        var links = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/{ev.Id}/invitation-links",
            organizerSession.AccessToken));
        links.StatusCode.Should().Be(HttpStatusCode.OK);
        var linksBody = await app.ReadApiResponseAsync<IEnumerable<EventInvitationLinkResponse>>(links);
        linksBody.Data.Should().ContainSingle(item => item.Id == createdBody.Data.Id);

        var anonymousResolve = await app.Client.PostAsJsonAsync(
            "/api/events/invitations/resolve",
            new { token });
        anonymousResolve.StatusCode.Should().Be(HttpStatusCode.OK);
        var anonymousResolveBody = await app.ReadApiResponseAsync<EventInvitationResolveResponse>(anonymousResolve);
        anonymousResolveBody.Data!.State.Should().Be("LoginRequired");
        anonymousResolveBody.Data.RequiresAuthentication.Should().BeTrue();

        var authenticatedResolve = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/invitations/resolve",
            inviteeSession.AccessToken,
            JsonContent.Create(new { token })));
        authenticatedResolve.StatusCode.Should().Be(HttpStatusCode.OK);
        var authenticatedResolveBody = await app.ReadApiResponseAsync<EventInvitationResolveResponse>(authenticatedResolve);
        authenticatedResolveBody.Data!.State.Should().Be("AcceptAvailable");
        authenticatedResolveBody.Data.CanAccept.Should().BeTrue();
        authenticatedResolveBody.Data.CanDecline.Should().BeTrue();

        var accepted = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/invitations/accept",
            inviteeSession.AccessToken,
            JsonContent.Create(new { token })));
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        var acceptedBody = await app.ReadApiResponseAsync<EventInvitationDecisionResponse>(accepted);
        acceptedBody.Data!.Invitation.SourceType.Should().Be("LinkClaim");
        acceptedBody.Data.Invitation.EffectiveStatus.Should().Be("Accepted");

        (await app.QueryDbAsync(db => db.EventInvitations.AnyAsync(i =>
            i.Id == acceptedBody.Data.Invitation.Id &&
            i.EventId == ev.Id &&
            i.LifecycleStatus == EventInvitationLifecycleStatus.Accepted))).Should().BeTrue();

        var myInvitations = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/events/me/invited",
            inviteeSession.AccessToken));
        myInvitations.StatusCode.Should().Be(HttpStatusCode.OK);
        var myInvitationsBody = await app.ReadApiResponseAsync<IEnumerable<EventInvitationResponse>>(myInvitations);
        myInvitationsBody.Data.Should().Contain(item =>
            item.EventId == ev.Id &&
            item.SourceType == "LinkClaim" &&
            item.EffectiveStatus == "Accepted");

        var revoked = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/invitation-links/{createdBody.Data.Id}/revoke",
            organizerSession.AccessToken));
        revoked.StatusCode.Should().Be(HttpStatusCode.OK);
        var revokedBody = await app.ReadApiResponseAsync<EventInvitationLinkResponse>(revoked);
        revokedBody.Data!.IsRevoked.Should().BeTrue();

        (await app.QueryDbAsync(db =>
            db.EventInvitationLinks.Where(l => l.Id == createdBody.Data.Id).Select(l => l.RevokedAtUtc).SingleAsync()))
            .Should().NotBeNull();

        var revokedResolve = await app.Client.PostAsJsonAsync(
            "/api/events/invitations/resolve",
            new { token });
        revokedResolve.StatusCode.Should().Be(HttpStatusCode.OK);
        var revokedResolveBody = await app.ReadApiResponseAsync<EventInvitationResolveResponse>(revokedResolve);
        revokedResolveBody.Data!.State.Should().Be("Revoked");
    }

    [Fact]
    public async Task DirectInvitationDecisionEndpoints_ShouldAcceptAndDecline_ByTokenAndById()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "decision-owner@example.com", "Organizer");
        var (acceptByTokenSession, acceptByTokenUser) = await CreateUserSessionAsync(app, "accept-token@example.com");
        var (acceptByIdSession, acceptByIdUser) = await CreateUserSessionAsync(app, "accept-id@example.com");
        var (declineByTokenSession, declineByTokenUser) = await CreateUserSessionAsync(app, "decline-token@example.com");
        var (declineByIdSession, declineByIdUser) = await CreateUserSessionAsync(app, "decline-id@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Decision Flows Club");
        var ev = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Private Decision Event",
            isPrivate: true);

        app.Publisher.Clear();

        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/invitations",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                userIds = new[]
                {
                    acceptByTokenUser!.Id,
                    acceptByIdUser!.Id,
                    declineByTokenUser!.Id,
                    declineByIdUser!.Id
                },
                expiresAt = DateTime.UtcNow.AddDays(4)
            })));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await app.ReadApiResponseAsync<IEnumerable<EventInvitationResponse>>(created);
        var invitationsByUserId = createdBody.Data!.ToDictionary(item => item.RecipientUserId!.Value);

        var acceptByTokenMessage = app.Publisher.EmailMessages.Single(message => message.Email == acceptByTokenUser.Email);
        var acceptByIdInvitation = invitationsByUserId[acceptByIdUser.Id];
        var declineByTokenMessage = app.Publisher.EmailMessages.Single(message => message.Email == declineByTokenUser.Email);
        var declineByIdInvitation = invitationsByUserId[declineByIdUser.Id];

        var resolved = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/invitations/resolve",
            acceptByTokenSession.AccessToken,
            JsonContent.Create(new { token = acceptByTokenMessage.Token })));
        resolved.StatusCode.Should().Be(HttpStatusCode.OK);
        var resolvedBody = await app.ReadApiResponseAsync<EventInvitationResolveResponse>(resolved);
        resolvedBody.Data!.State.Should().Be("AcceptAvailable");

        var acceptedByToken = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/invitations/accept",
            acceptByTokenSession.AccessToken,
            JsonContent.Create(new { token = acceptByTokenMessage.Token })));
        acceptedByToken.StatusCode.Should().Be(HttpStatusCode.OK);
        var acceptedByTokenBody = await app.ReadApiResponseAsync<EventInvitationDecisionResponse>(acceptedByToken);
        acceptedByTokenBody.Data!.Invitation.EffectiveStatus.Should().Be("Accepted");

        var acceptedById = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/invitations/{acceptByIdInvitation.Id}/accept",
            acceptByIdSession.AccessToken));
        acceptedById.StatusCode.Should().Be(HttpStatusCode.OK);
        var acceptedByIdBody = await app.ReadApiResponseAsync<EventInvitationDecisionResponse>(acceptedById);
        acceptedByIdBody.Data!.Invitation.EffectiveStatus.Should().Be("Accepted");

        var declinedByToken = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/invitations/decline",
            declineByTokenSession.AccessToken,
            JsonContent.Create(new { token = declineByTokenMessage.Token })));
        declinedByToken.StatusCode.Should().Be(HttpStatusCode.OK);
        var declinedByTokenBody = await app.ReadApiResponseAsync<EventInvitationDecisionResponse>(declinedByToken);
        declinedByTokenBody.Data!.Invitation.EffectiveStatus.Should().Be("Declined");

        var declinedById = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/invitations/{declineByIdInvitation.Id}/decline",
            declineByIdSession.AccessToken));
        declinedById.StatusCode.Should().Be(HttpStatusCode.OK);
        var declinedByIdBody = await app.ReadApiResponseAsync<EventInvitationDecisionResponse>(declinedById);
        declinedByIdBody.Data!.Invitation.EffectiveStatus.Should().Be("Declined");

        var persistedStatuses = await app.QueryDbAsync(db => db.EventInvitations
            .Where(i => i.EventId == ev.Id)
            .ToDictionaryAsync(i => i.RecipientUserId!.Value, i => i.LifecycleStatus));
        persistedStatuses[acceptByTokenUser.Id].Should().Be(EventInvitationLifecycleStatus.Accepted);
        persistedStatuses[acceptByIdUser.Id].Should().Be(EventInvitationLifecycleStatus.Accepted);
        persistedStatuses[declineByTokenUser.Id].Should().Be(EventInvitationLifecycleStatus.Declined);
        persistedStatuses[declineByIdUser.Id].Should().Be(EventInvitationLifecycleStatus.Declined);
    }

    [Fact]
    public async Task InvitationEndpoints_ShouldRejectPublicEvents_AndWrongRecipientAccess()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizerSession, _) = await CreateUserSessionAsync(app, "edge-owner@example.com", "Organizer");
        var (invitedSession, invitedUser) = await CreateUserSessionAsync(app, "edge-invited@example.com");
        var (outsiderSession, _) = await CreateUserSessionAsync(app, "edge-outsider@example.com");

        var club = await CreateClubAsync(app, organizerSession.AccessToken, "Edge Cases Club");
        var publicEvent = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Public Event",
            isPrivate: false);

        var invalidCreate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{publicEvent.Id}/invitations",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                userIds = new[] { invitedUser!.Id }
            })));
        invalidCreate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidCreate.Content.ReadAsStringAsync()).Should().Contain("Invitations are only supported for private events.");

        var privateEvent = await CreateEventAsync(
            app,
            organizerSession.AccessToken,
            club.Id,
            "Private Event",
            isPrivate: true);

        app.Publisher.Clear();

        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{privateEvent.Id}/invitations",
            organizerSession.AccessToken,
            JsonContent.Create(new
            {
                userIds = new[] { invitedUser.Id },
                expiresAt = DateTime.UtcNow.AddDays(2)
            })));
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var invitation = (await app.ReadApiResponseAsync<IEnumerable<EventInvitationResponse>>(created)).Data!.Single();
        var token = app.Publisher.EmailMessages.Single(message => message.Email == invitedUser.Email).Token;

        var invalidResolve = await app.Client.PostAsJsonAsync(
            "/api/events/invitations/resolve",
            new { token = "not-a-real-token" });
        invalidResolve.StatusCode.Should().Be(HttpStatusCode.OK);
        var invalidResolveBody = await app.ReadApiResponseAsync<EventInvitationResolveResponse>(invalidResolve);
        invalidResolveBody.Data!.State.Should().Be("Invalid");

        var wrongUserAcceptByToken = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/invitations/accept",
            outsiderSession.AccessToken,
            JsonContent.Create(new { token })));
        wrongUserAcceptByToken.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var wrongUserAcceptById = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/invitations/{invitation.Id}/accept",
            outsiderSession.AccessToken));
        wrongUserAcceptById.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
                Description = "Invitation test group",
                Clubtype = "social",
                ClubImageUrl = app.BlobStorage.CreateOwnedBlobUrl("clubs", "club.png"),
                Email = $"{name.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}@example.com"
            })));
        var diagnostics = await app.DescribeFailureAsync(response);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new Xunit.Sdk.XunitException(diagnostics);
        }

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
                description = "A detailed private event description for invitation endpoint integration tests.",
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
                StartTime = managed.StartTime ?? DateTime.UtcNow,
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

    private static string ExtractToken(string shareUrl)
    {
        var uri = new Uri($"https://localhost{shareUrl}");
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty,
                StringComparer.Ordinal);

        return query.TryGetValue("token", out var token)
            ? token
            : throw new InvalidOperationException("Invitation share URL did not include a token.");
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
}
