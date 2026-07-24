using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.follow.invitations.contracts.responses;
using backend.main.shared.providers.messages;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Clubs;

public class ClubMemberInvitationEndpointsTests
{
    [Fact]
    public async Task SpecificMemberInvitationFlow_ShouldCreateListResolveAccept_AndDeclineRevoke()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "member-invite-owner@example.com", "Organizer");
        var (inviteeSession, invitee) = await CreateUserSessionAsync(app, "member-invitee@example.com");
        var (declineSession, declineUser) = await CreateUserSessionAsync(app, "member-decline@example.com");
        var (_, revokeUser) = await CreateUserSessionAsync(app, "member-revoke@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Member Invitation Club");

        app.Publisher.Clear();

        // Create — invite the existing user by email.
        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/members/invitations",
            ownerSession.AccessToken,
            JsonContent.Create(new { identifier = invitee!.Email })));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await app.ReadApiResponseAsync<ClubMemberInvitationResponse>(created);
        createdBody.Data!.RecipientUserId.Should().Be(invitee.Id);
        createdBody.Data.RecipientEmail.Should().Be(invitee.Email);

        var inviteMessage = app.Publisher.EmailMessages.Single(message =>
            message.Type == EmailMessageType.ClubMemberInvite &&
            message.Email == invitee.Email);
        var token = inviteMessage.Token;
        token.Should().NotBeNullOrWhiteSpace();

        // List — the owner sees the pending invitation.
        var list = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/members/invitations",
            ownerSession.AccessToken));
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await app.ReadApiResponseAsync<IEnumerable<ClubMemberInvitationResponse>>(list);
        listBody.Data.Should().ContainSingle(item => item.RecipientUserId == invitee.Id);

        // Resolve — anonymous requires authentication.
        var anonymousResolve = await app.Client.PostAsJsonAsync(
            "/api/clubs/members/invitations/resolve",
            new { token });
        anonymousResolve.StatusCode.Should().Be(HttpStatusCode.OK);
        var anonymousResolveBody = await app.ReadApiResponseAsync<ClubMemberInvitationResolveResponse>(anonymousResolve);
        anonymousResolveBody.Data!.State.Should().Be("LoginRequired");
        anonymousResolveBody.Data.Source.Should().Be("DirectInvite");
        anonymousResolveBody.Data.RequiresAuthentication.Should().BeTrue();

        // Resolve — the recipient can accept.
        var recipientResolve = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs/members/invitations/resolve",
            inviteeSession.AccessToken,
            JsonContent.Create(new { token })));
        recipientResolve.StatusCode.Should().Be(HttpStatusCode.OK);
        var recipientResolveBody = await app.ReadApiResponseAsync<ClubMemberInvitationResolveResponse>(recipientResolve);
        recipientResolveBody.Data!.State.Should().Be("AcceptAvailable");
        recipientResolveBody.Data.CanAccept.Should().BeTrue();
        recipientResolveBody.Data.Club!.Name.Should().Be("Member Invitation Club");

        // Accept — grants membership.
        var accepted = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs/members/invitations/accept",
            inviteeSession.AccessToken,
            JsonContent.Create(new { token })));
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        var acceptedBody = await app.ReadApiResponseAsync<ClubMemberInvitationDecisionResponse>(accepted);
        acceptedBody.Data!.Accepted.Should().BeTrue();
        acceptedBody.Data.ClubId.Should().Be(club.Id);

        (await app.QueryDbAsync(db => db.FollowClubs.AnyAsync(f =>
            f.ClubId == club.Id && f.UserId == invitee.Id))).Should().BeTrue();

        // Decline — a second recipient rejects the invite; no membership row is created.
        app.Publisher.Clear();
        await CreateInviteAsync(app, ownerSession.AccessToken, club.Id, declineUser!.Email);
        var declineToken = app.Publisher.EmailMessages.Single(m => m.Email == declineUser.Email).Token;

        var declined = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs/members/invitations/decline",
            declineSession.AccessToken,
            JsonContent.Create(new { token = declineToken })));
        declined.StatusCode.Should().Be(HttpStatusCode.OK);
        var declinedBody = await app.ReadApiResponseAsync<ClubMemberInvitationDecisionResponse>(declined);
        declinedBody.Data!.Accepted.Should().BeFalse();
        (await app.QueryDbAsync(db => db.FollowClubs.AnyAsync(f =>
            f.ClubId == club.Id && f.UserId == declineUser.Id))).Should().BeFalse();

        // Revoke — the owner cancels a pending invitation.
        await CreateInviteAsync(app, ownerSession.AccessToken, club.Id, revokeUser!.Email);
        var revoked = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/members/invitations/{revokeUser.Id}/revoke",
            ownerSession.AccessToken));
        revoked.StatusCode.Should().Be(HttpStatusCode.OK);

        var listAfterRevoke = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/members/invitations",
            ownerSession.AccessToken));
        var listAfterRevokeBody = await app.ReadApiResponseAsync<IEnumerable<ClubMemberInvitationResponse>>(listAfterRevoke);
        listAfterRevokeBody.Data.Should().NotContain(item => item.RecipientUserId == revokeUser.Id);
    }

    [Fact]
    public async Task MemberInviteLinkFlow_ShouldCreateListResolveRedeem_AndRevoke()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "link-owner@example.com", "Organizer");
        var (joinerSession, joiner) = await CreateUserSessionAsync(app, "link-joiner@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Member Link Club");

        // Create — mint a shareable link with an optional cap.
        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/members/invitation-links",
            ownerSession.AccessToken,
            JsonContent.Create(new { expiresAt = DateTime.UtcNow.AddDays(3), maxRedemptions = 5 })));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await app.ReadApiResponseAsync<ClubInvitationLinkResponse>(created);
        createdBody.Data!.MaxRedemptions.Should().Be(5);
        createdBody.Data.ShareUrl.Should().StartWith("/clubs/member-invite?token=");
        var linkId = createdBody.Data.Id;
        var token = ExtractToken(createdBody.Data.ShareUrl!);

        // List — the owner sees the active link.
        var list = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/members/invitation-links",
            ownerSession.AccessToken));
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await app.ReadApiResponseAsync<IEnumerable<ClubInvitationLinkResponse>>(list);
        listBody.Data.Should().ContainSingle(item => item.Id == linkId);

        // Resolve — any authenticated user may redeem a link.
        var resolve = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs/members/invitations/resolve",
            joinerSession.AccessToken,
            JsonContent.Create(new { token })));
        resolve.StatusCode.Should().Be(HttpStatusCode.OK);
        var resolveBody = await app.ReadApiResponseAsync<ClubMemberInvitationResolveResponse>(resolve);
        resolveBody.Data!.State.Should().Be("AcceptAvailable");
        resolveBody.Data.Source.Should().Be("Link");
        resolveBody.Data.CanDecline.Should().BeFalse();

        // Redeem — grants membership and increments the redemption count.
        var redeemed = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs/members/invitation-links/redeem",
            joinerSession.AccessToken,
            JsonContent.Create(new { token })));
        redeemed.StatusCode.Should().Be(HttpStatusCode.OK);
        var redeemedBody = await app.ReadApiResponseAsync<ClubMemberInvitationDecisionResponse>(redeemed);
        redeemedBody.Data!.Accepted.Should().BeTrue();
        redeemedBody.Data.ClubId.Should().Be(club.Id);

        (await app.QueryDbAsync(db => db.FollowClubs.AnyAsync(f =>
            f.ClubId == club.Id && f.UserId == joiner!.Id))).Should().BeTrue();

        // Revoke — the owner disables the link.
        var revoked = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/members/invitation-links/{linkId}/revoke",
            ownerSession.AccessToken));
        revoked.StatusCode.Should().Be(HttpStatusCode.OK);
        var revokedBody = await app.ReadApiResponseAsync<ClubInvitationLinkResponse>(revoked);
        revokedBody.Data!.IsRevoked.Should().BeTrue();
    }

    private static string ExtractToken(string shareUrl)
    {
        var marker = "token=";
        var index = shareUrl.IndexOf(marker, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0);
        return Uri.UnescapeDataString(shareUrl[(index + marker.Length)..]);
    }

    private static async Task CreateInviteAsync(
        AuthApiTestApp app,
        string ownerAccessToken,
        int clubId,
        string identifier)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/members/invitations",
            ownerAccessToken,
            JsonContent.Create(new { identifier })));
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
                Description = "Member invitation test group",
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
