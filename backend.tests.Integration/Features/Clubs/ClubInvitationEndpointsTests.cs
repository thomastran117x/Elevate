using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.invitations.contracts.responses;
using backend.main.features.clubs.staff;
using backend.main.shared.providers.messages;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Clubs;

public class ClubInvitationEndpointsTests
{
    [Fact]
    public async Task StaffInvitationFlow_ShouldCreateListResolveAndAccept_MaterializingStaff()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "club-invite-owner@example.com", "Organizer");
        var (inviteeSession, invitee) = await CreateUserSessionAsync(app, "club-invitee@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Staff Invitation Club");

        app.Publisher.Clear();

        // Create — invite the existing user by email.
        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/staff/invitations",
            ownerSession.AccessToken,
            JsonContent.Create(new { identifier = invitee!.Email, role = "Manager" })));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await app.ReadApiResponseAsync<ClubInvitationResponse>(created);
        createdBody.Data!.RecipientUserId.Should().Be(invitee.Id);
        createdBody.Data.RecipientEmail.Should().Be(invitee.Email);
        createdBody.Data.Role.Should().Be("Manager");

        var inviteMessage = app.Publisher.EmailMessages.Single(message =>
            message.Type == EmailMessageType.ClubStaffInvite &&
            message.Email == invitee.Email);
        var token = inviteMessage.Token;
        token.Should().NotBeNullOrWhiteSpace();

        // List — the owner sees the pending invitation.
        var list = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/staff/invitations",
            ownerSession.AccessToken));
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await app.ReadApiResponseAsync<IEnumerable<ClubInvitationResponse>>(list);
        listBody.Data.Should().ContainSingle(item => item.RecipientUserId == invitee.Id);

        // Resolve — anonymous requires authentication.
        var anonymousResolve = await app.Client.PostAsJsonAsync(
            "/api/clubs/invitations/resolve",
            new { token });
        anonymousResolve.StatusCode.Should().Be(HttpStatusCode.OK);
        var anonymousResolveBody = await app.ReadApiResponseAsync<ClubInvitationResolveResponse>(anonymousResolve);
        anonymousResolveBody.Data!.State.Should().Be("LoginRequired");
        anonymousResolveBody.Data.RequiresAuthentication.Should().BeTrue();

        // Resolve — the recipient can accept.
        var recipientResolve = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs/invitations/resolve",
            inviteeSession.AccessToken,
            JsonContent.Create(new { token })));
        recipientResolve.StatusCode.Should().Be(HttpStatusCode.OK);
        var recipientResolveBody = await app.ReadApiResponseAsync<ClubInvitationResolveResponse>(recipientResolve);
        recipientResolveBody.Data!.State.Should().Be("AcceptAvailable");
        recipientResolveBody.Data.CanAccept.Should().BeTrue();
        recipientResolveBody.Data.Club!.Name.Should().Be("Staff Invitation Club");

        // Accept — grants the staff role.
        var accepted = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs/invitations/accept",
            inviteeSession.AccessToken,
            JsonContent.Create(new { token })));
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        var acceptedBody = await app.ReadApiResponseAsync<ClubInvitationDecisionResponse>(accepted);
        acceptedBody.Data!.Accepted.Should().BeTrue();
        acceptedBody.Data.ClubId.Should().Be(club.Id);
        acceptedBody.Data.Role.Should().Be("Manager");

        (await app.QueryDbAsync(db => db.ClubStaff.AnyAsync(s =>
            s.ClubId == club.Id &&
            s.UserId == invitee.Id &&
            s.Role == ClubStaffRole.Manager))).Should().BeTrue();

        // The invitation is consumed once accepted.
        var listAfter = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/staff/invitations",
            ownerSession.AccessToken));
        var listAfterBody = await app.ReadApiResponseAsync<IEnumerable<ClubInvitationResponse>>(listAfter);
        listAfterBody.Data.Should().NotContain(item => item.RecipientUserId == invitee.Id);
    }

    [Fact]
    public async Task StaffInvitationFlow_ShouldDeclineRevoke_AndGuardWrongRecipientAndUnknownUser()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "club-guard-owner@example.com", "Organizer");
        var (declineSession, declineUser) = await CreateUserSessionAsync(app, "club-decline@example.com");
        var (_, revokeUser) = await CreateUserSessionAsync(app, "club-revoke@example.com");
        var (_, guardUser) = await CreateUserSessionAsync(app, "club-guard-target@example.com");
        var (outsiderSession, _) = await CreateUserSessionAsync(app, "club-outsider@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Staff Guard Club");

        // Decline — the recipient rejects the invite; no staff row is created.
        app.Publisher.Clear();
        await CreateInviteAsync(app, ownerSession.AccessToken, club.Id, declineUser!.Email, "Volunteer");
        var declineToken = app.Publisher.EmailMessages.Single(m => m.Email == declineUser.Email).Token;

        var declined = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs/invitations/decline",
            declineSession.AccessToken,
            JsonContent.Create(new { token = declineToken })));
        declined.StatusCode.Should().Be(HttpStatusCode.OK);
        var declinedBody = await app.ReadApiResponseAsync<ClubInvitationDecisionResponse>(declined);
        declinedBody.Data!.Accepted.Should().BeFalse();
        (await app.QueryDbAsync(db => db.ClubStaff.AnyAsync(s =>
            s.ClubId == club.Id && s.UserId == declineUser.Id))).Should().BeFalse();

        // Revoke — the owner cancels a pending invitation.
        await CreateInviteAsync(app, ownerSession.AccessToken, club.Id, revokeUser!.Email, "Manager");
        var revoked = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/staff/invitations/{revokeUser.Id}/revoke",
            ownerSession.AccessToken));
        revoked.StatusCode.Should().Be(HttpStatusCode.OK);

        var listAfterRevoke = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/staff/invitations",
            ownerSession.AccessToken));
        var listAfterRevokeBody = await app.ReadApiResponseAsync<IEnumerable<ClubInvitationResponse>>(listAfterRevoke);
        listAfterRevokeBody.Data.Should().NotContain(item => item.RecipientUserId == revokeUser.Id);

        // Guard — a different user cannot accept someone else's token.
        app.Publisher.Clear();
        await CreateInviteAsync(app, ownerSession.AccessToken, club.Id, guardUser!.Email, "Manager");
        var guardToken = app.Publisher.EmailMessages.Single(m => m.Email == guardUser.Email).Token;

        var wrongRecipient = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs/invitations/accept",
            outsiderSession.AccessToken,
            JsonContent.Create(new { token = guardToken })));
        wrongRecipient.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Unknown identifier — no such account.
        var unknown = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/staff/invitations",
            ownerSession.AccessToken,
            JsonContent.Create(new { identifier = "nobody@nowhere.example", role = "Manager" })));
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task CreateInviteAsync(
        AuthApiTestApp app,
        string ownerAccessToken,
        int clubId,
        string identifier,
        string role)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/staff/invitations",
            ownerAccessToken,
            JsonContent.Create(new { identifier, role })));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
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
                Description = "Club invitation test group",
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
