using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.follow.invitations.contracts.responses;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments.contracts.responses;
using backend.main.features.clubs.posts.contracts.responses;
using backend.main.features.clubs.reviews.contracts.responses;
using backend.main.shared.providers.messages;
using backend.main.shared.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Workflows;

[Trait("Category", "EndToEnd")]
public class ClubCommunityWorkflowTests
{
    [Fact]
    public async Task DirectMemberInvitation_ShouldGrantMembershipAndUpdateTheClubCommunity()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateUserAsync(app, "workflow-club-owner@example.com", "Organizer");
        var invitee = await CreateUserAsync(app, "workflow-club-invitee@example.com");

        var club = await CreateClubAsync(
            app,
            owner.Session.AccessToken,
            "Invitation Community Club",
            "workflow-club-invite@example.com");
        club.MemberCount.Should().Be(0);

        app.Publisher.Clear();

        var createInvitation = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/members/invitations",
            owner.Session.AccessToken,
            JsonContent.Create(new { identifier = "workflow-club-invitee@example.com" })));
        createInvitation.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await app.DescribeFailureAsync(createInvitation));
        var invitation = (await app.ReadApiResponseAsync<ClubMemberInvitationResponse>(createInvitation)).Data!;
        invitation.RecipientUserId.Should().Be(invitee.UserId);

        var inviteEmail = app.Publisher.EmailMessages.Should()
            .ContainSingle(message =>
                message.Type == EmailMessageType.ClubMemberInvite &&
                message.Email == "workflow-club-invitee@example.com")
            .Subject;
        inviteEmail.Token.Should().NotBeNullOrWhiteSpace();

        var resolved = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs/members/invitations/resolve",
            invitee.Session.AccessToken,
            JsonContent.Create(new { token = inviteEmail.Token })));
        resolved.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(resolved));
        var resolvedBody = await app.ReadApiResponseAsync<ClubMemberInvitationResolveResponse>(resolved);
        resolvedBody.Data.Should().Match<ClubMemberInvitationResolveResponse>(item =>
            item.State == "AcceptAvailable" &&
            item.CanAccept &&
            item.Club != null &&
            item.Club.Name == "Invitation Community Club");

        var accepted = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs/members/invitations/accept",
            invitee.Session.AccessToken,
            JsonContent.Create(new { token = inviteEmail.Token })));
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(accepted));
        var acceptedBody = await app.ReadApiResponseAsync<ClubMemberInvitationDecisionResponse>(accepted);
        acceptedBody.Data.Should().Match<ClubMemberInvitationDecisionResponse>(item =>
            item.Accepted && item.ClubId == club.Id);

        var membership = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/members/me",
            invitee.Session.AccessToken));
        membership.StatusCode.Should().Be(HttpStatusCode.OK);
        (await membership.Content.ReadAsStringAsync()).Should().Contain("\"isMember\":true");

        var clubAfterAcceptance = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}",
            owner.Session.AccessToken));
        clubAfterAcceptance.StatusCode.Should().Be(HttpStatusCode.OK);
        var clubAfterAcceptanceBody = await app.ReadApiResponseAsync<WorkflowClubResponse>(clubAfterAcceptance);
        clubAfterAcceptanceBody.Data!.MemberCount.Should().Be(1);

        (await app.QueryDbAsync(db => db.FollowClubs.AnyAsync(item =>
            item.ClubId == club.Id && item.UserId == invitee.UserId))).Should().BeTrue();
        (await app.QueryDbAsync(db => db.Clubs
            .Where(item => item.Id == club.Id)
            .Select(item => item.MemberCount)
            .SingleAsync())).Should().Be(1);
    }

    [Fact]
    public async Task MemberParticipation_ShouldConnectMembershipPostsCommentsReviewsAndClubMetrics()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateUserAsync(app, "workflow-community-owner@example.com", "Organizer");
        var member = await CreateUserAsync(app, "workflow-community-member@example.com");

        var club = await CreateClubAsync(
            app,
            owner.Session.AccessToken,
            "Active Community Club",
            "workflow-community-club@example.com");

        var join = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/join",
            member.Session.AccessToken));
        join.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(join));

        var createPost = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/posts",
            owner.Session.AccessToken,
            JsonContent.Create(new
            {
                title = "Community Planning Night",
                content = "Share ideas for our next community gathering.",
                postType = PostType.Announcement,
                isPinned = true
            })));
        createPost.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(createPost));
        var post = (await app.ReadApiResponseAsync<ClubPostResponse>(createPost)).Data!;

        var createComment = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/posts/{post.Id}/comments",
            member.Session.AccessToken,
            JsonContent.Create(new { content = "I can help organize the venue." })));
        createComment.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(createComment));
        var comment = (await app.ReadApiResponseAsync<PostCommentResponse>(createComment)).Data!;
        comment.UserId.Should().Be(member.UserId);

        var createReview = await app.Client.SendAsync(AuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/reviews",
            member.Session.AccessToken,
            JsonContent.Create(new
            {
                title = "A welcoming community",
                rating = 5,
                comment = "The organizers make it easy to participate."
            })));
        createReview.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(createReview));
        var review = (await app.ReadApiResponseAsync<ClubReviewResponse>(createReview)).Data!;
        review.UserId.Should().Be(member.UserId);

        var publicPosts = await app.Client.GetAsync($"/api/clubs/{club.Id}/posts");
        publicPosts.StatusCode.Should().Be(HttpStatusCode.OK);
        var publicPostsBody = await app.ReadApiResponseAsync<PagedResponse<ClubPostResponse>>(publicPosts);
        publicPostsBody.Data!.Items.Should().ContainSingle(item =>
            item.Id == post.Id && item.IsPinned && item.PostType == PostType.Announcement);

        var publicComments = await app.Client.GetAsync($"/api/clubs/{club.Id}/posts/{post.Id}/comments");
        publicComments.StatusCode.Should().Be(HttpStatusCode.OK);
        var publicCommentsBody = await app.ReadApiResponseAsync<PagedResponse<PostCommentResponse>>(publicComments);
        publicCommentsBody.Data!.Items.Should().ContainSingle(item =>
            item.Id == comment.Id && item.Content == "I can help organize the venue.");

        var publicReviews = await app.Client.GetAsync($"/api/clubs/{club.Id}/reviews");
        publicReviews.StatusCode.Should().Be(HttpStatusCode.OK);
        var publicReviewsBody = await app.ReadApiResponseAsync<PagedResponse<ClubReviewResponse>>(publicReviews);
        publicReviewsBody.Data!.Items.Should().ContainSingle(item => item.Id == review.Id && item.Rating == 5);

        var clubDetail = await app.Client.GetAsync($"/api/clubs/{club.Id}");
        clubDetail.StatusCode.Should().Be(HttpStatusCode.OK);
        var clubDetailBody = await app.ReadApiResponseAsync<WorkflowClubResponse>(clubDetail);
        clubDetailBody.Data.Should().Match<WorkflowClubResponse>(item =>
            item.MemberCount == 1 && item.Rating == 5.0);

        (await app.QueryDbAsync(db => db.FollowClubs.AnyAsync(item =>
            item.ClubId == club.Id && item.UserId == member.UserId))).Should().BeTrue();
        (await app.QueryDbAsync(db => db.PostComments.AnyAsync(item =>
            item.Id == comment.Id && item.UserId == member.UserId))).Should().BeTrue();
        (await app.QueryDbAsync(db => db.ClubReviews.AnyAsync(item =>
            item.Id == review.Id && item.UserId == member.UserId && item.Rating == 5))).Should().BeTrue();
    }

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
                description = "A club created by an end-to-end community workflow.",
                clubtype = "social",
                clubImageUrl = app.BlobStorage.CreateOwnedBlobUrl("clubs", "community-workflow.png"),
                email
            })));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<WorkflowClubResponse>(response)).Data!;
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
        public int MemberCount { get; init; }
        public double? Rating { get; init; }
    }
}
