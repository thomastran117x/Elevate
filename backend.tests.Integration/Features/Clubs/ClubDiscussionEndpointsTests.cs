using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.discussions.contracts.responses;
using backend.main.features.clubs.discussions.replies.contracts.responses;
using backend.main.shared.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Clubs;

public class ClubDiscussionEndpointsTests
{
    [Fact]
    public async Task ReplyEndpoints_ShouldSupportNestedRepliesReactionsEditingAndSoftDeletion()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateUserSessionAsync(app, "reply-owner@example.com", "Organizer");
        var participant = await CreateUserSessionAsync(app, "reply-participant@example.com");
        var clubId = await CreateClubAsync(app, owner.AccessToken, "Reply Club");

        var discussionResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions",
            owner.AccessToken,
            JsonContent.Create(new { title = "Live topic", description = "Talk here." })));
        var discussion = (await app.ReadApiResponseAsync<ClubDiscussionResponse>(discussionResponse)).Data!;

        // Public clubs allow any authenticated user to participate without joining.
        var rootResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies",
            participant.AccessToken,
            JsonContent.Create(new { content = " Root reply ", parentReplyId = (int?)null })));
        rootResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var root = (await app.ReadApiResponseAsync<DiscussionReplyResponse>(rootResponse)).Data!;
        root.Content.Should().Be("Root reply");

        var childResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies",
            owner.AccessToken,
            JsonContent.Create(new { content = "Nested reply", parentReplyId = root.Id })));
        var child = (await app.ReadApiResponseAsync<DiscussionReplyResponse>(childResponse)).Data!;

        var grandchildResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies",
            participant.AccessToken,
            JsonContent.Create(new { content = "Unlimited depth", parentReplyId = child.Id })));
        grandchildResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var reaction = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies/{root.Id}/reaction",
            owner.AccessToken,
            JsonContent.Create(new { reaction = "Like" })));
        reaction.StatusCode.Should().Be(HttpStatusCode.OK);
        var reactionData = (await app.ReadApiResponseAsync<DiscussionReplyReactionResponse>(reaction)).Data!;
        reactionData.LikeCount.Should().Be(1);
        reactionData.CurrentUserReaction.Should().Be("Like");

        var switched = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies/{root.Id}/reaction",
            owner.AccessToken,
            JsonContent.Create(new { reaction = "Dislike" })));
        var switchedData = (await app.ReadApiResponseAsync<DiscussionReplyReactionResponse>(switched)).Data!;
        switchedData.LikeCount.Should().Be(0);
        switchedData.DislikeCount.Should().Be(1);

        var edited = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies/{root.Id}",
            participant.AccessToken,
            JsonContent.Create(new { content = "Edited root" })));
        edited.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleted = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies/{root.Id}",
            participant.AccessToken));
        var placeholder = (await app.ReadApiResponseAsync<DiscussionReplyResponse>(deleted)).Data!;
        placeholder.IsDeleted.Should().BeTrue();
        placeholder.Content.Should().BeNull();
        placeholder.Author.Should().BeNull();
        placeholder.DislikeCount.Should().Be(0);

        var children = await app.Client.GetAsync(
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies?parentReplyId={root.Id}&sort=Oldest");
        children.StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.ReadApiResponseAsync<DiscussionReplyPageResponse>(children))
            .Data!.Items.Should().ContainSingle(r => r.Id == child.Id);

        var replyToDeleted = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies",
            owner.AccessToken,
            JsonContent.Create(new { content = "Not allowed", parentReplyId = root.Id })));
        replyToDeleted.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var discussions = await app.Client.GetAsync($"/api/clubs/{clubId}/discussions");
        var listed = (await app.ReadApiResponseAsync<PagedResponse<ClubDiscussionResponse>>(discussions))
            .Data!.Items.Single();
        listed.ReplyCount.Should().Be(3);
    }

    [Fact]
    public async Task ClearReaction_ShouldReturnNeutralStateAndRemoveOnlyTheCurrentUsersReaction()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateUserSessionAsync(app, "clear-reaction-owner@example.com", "Organizer");
        var participant = await CreateUserSessionAsync(app, "clear-reaction-participant@example.com");
        var clubId = await CreateClubAsync(app, owner.AccessToken, "Clear Reaction Club");

        var discussionResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions",
            owner.AccessToken,
            JsonContent.Create(new { title = "Reaction topic", description = "Test clearing reactions." })));
        var discussion = (await app.ReadApiResponseAsync<ClubDiscussionResponse>(discussionResponse)).Data!;

        var replyResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies",
            owner.AccessToken,
            JsonContent.Create(new { content = "React to this reply" })));
        var reply = (await app.ReadApiResponseAsync<DiscussionReplyResponse>(replyResponse)).Data!;
        var reactionPath =
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies/{reply.Id}/reaction";

        foreach (var accessToken in new[] { owner.AccessToken, participant.AccessToken })
        {
            var setReaction = await app.Client.SendAsync(CreateAuthorizedRequest(
                HttpMethod.Put,
                reactionPath,
                accessToken,
                JsonContent.Create(new { reaction = "Like" })));
            setReaction.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var cleared = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            reactionPath,
            owner.AccessToken));

        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        var clearedData = (await app.ReadApiResponseAsync<DiscussionReplyReactionResponse>(cleared)).Data!;
        clearedData.ReplyId.Should().Be(reply.Id);
        clearedData.LikeCount.Should().Be(1);
        clearedData.DislikeCount.Should().Be(0);
        clearedData.CurrentUserReaction.Should().BeNull();

        (await app.QueryDbAsync(db => db.ClubDiscussionReplyReactions
            .CountAsync(reaction => reaction.ReplyId == reply.Id)))
            .Should().Be(1);

        var repeatedClear = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            reactionPath,
            owner.AccessToken));
        repeatedClear.StatusCode.Should().Be(HttpStatusCode.OK);
        var repeatedData =
            (await app.ReadApiResponseAsync<DiscussionReplyReactionResponse>(repeatedClear)).Data!;
        repeatedData.LikeCount.Should().Be(1);
        repeatedData.CurrentUserReaction.Should().BeNull();
    }

    [Fact]
    public async Task DiscussionEndpoints_ShouldSupportCrudAndListNewestFirst()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var ownerSession = await CreateUserSessionAsync(app, "discussions-owner@example.com", "Organizer");
        var memberSession = await CreateUserSessionAsync(app, "discussions-member@example.com");

        var clubId = await CreateClubAsync(app, ownerSession.AccessToken, "Hiking Club");

        var joined = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/join",
            memberSession.AccessToken));
        joined.StatusCode.Should().Be(HttpStatusCode.OK);

        var createdFirst = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions",
            memberSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Weekend ride",
                description = "Where should we go this Saturday?"
            })));
        createdFirst.StatusCode.Should().Be(HttpStatusCode.Created);
        var first = (await app.ReadApiResponseAsync<ClubDiscussionResponse>(createdFirst)).Data!;
        first.Title.Should().Be("Weekend ride");
        first.ClubId.Should().Be(clubId);
        first.Author!.Id.Should().Be(first.UserId);

        var createdSecond = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions",
            memberSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Trail conditions",
                description = "Anyone been up the north ridge recently?"
            })));
        createdSecond.StatusCode.Should().Be(HttpStatusCode.Created);
        var second = (await app.ReadApiResponseAsync<ClubDiscussionResponse>(createdSecond)).Data!;

        // Public club: readable anonymously, newest first.
        var listed = await app.Client.GetAsync($"/api/clubs/{clubId}/discussions");
        listed.StatusCode.Should().Be(HttpStatusCode.OK);
        var listedBody = await app.ReadApiResponseAsync<PagedResponse<ClubDiscussionResponse>>(listed);
        var items = listedBody.Data!.Items.ToList();
        items.Should().HaveCount(2);
        items[0].Id.Should().Be(second.Id);
        items[1].Id.Should().Be(first.Id);
        listedBody.Data.TotalCount.Should().Be(2);

        var persisted = await app.QueryDbAsync(db =>
            db.ClubDiscussions.SingleOrDefaultAsync(d => d.Id == first.Id));
        persisted.Should().NotBeNull();
        persisted!.ClubId.Should().Be(clubId);
        persisted.Description.Should().Be("Where should we go this Saturday?");

        var updated = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{clubId}/discussions/{first.Id}",
            memberSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Weekend ride (updated)",
                description = "Sunday works better for me."
            })));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.ReadApiResponseAsync<ClubDiscussionResponse>(updated)).Data!
            .Title.Should().Be("Weekend ride (updated)");

        (await app.QueryDbAsync(db =>
            db.ClubDiscussions.Where(d => d.Id == first.Id).Select(d => d.Description).SingleAsync()))
            .Should().Be("Sunday works better for me.");

        var deleted = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{clubId}/discussions/{first.Id}",
            memberSession.AccessToken));
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.QueryDbAsync(db => db.ClubDiscussions.AnyAsync(d => d.Id == first.Id)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task DiscussionEndpoints_ShouldRejectNonMembersAndNonAuthors()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var ownerSession = await CreateUserSessionAsync(app, "discussions-auth-owner@example.com", "Organizer");
        var memberSession = await CreateUserSessionAsync(app, "discussions-auth-member@example.com");
        var outsiderSession = await CreateUserSessionAsync(app, "discussions-auth-outsider@example.com");

        var clubId = await CreateClubAsync(app, ownerSession.AccessToken, "Gated Club");

        // A signed-in non-member cannot author a discussion, even on a public club.
        var outsiderCreate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions",
            outsiderSession.AccessToken,
            JsonContent.Create(new { title = "Sneaky", description = "Not a member." })));
        outsiderCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var joined = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/join",
            memberSession.AccessToken));
        joined.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions",
            memberSession.AccessToken,
            JsonContent.Create(new { title = "Members only", description = "Started by a member." })));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var discussion = (await app.ReadApiResponseAsync<ClubDiscussionResponse>(created)).Data!;

        // Someone else's discussion is not theirs to edit or delete.
        var foreignUpdate = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{clubId}/discussions/{discussion.Id}",
            outsiderSession.AccessToken,
            JsonContent.Create(new { title = "Hijacked", description = "Nope." })));
        foreignUpdate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var foreignDelete = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{clubId}/discussions/{discussion.Id}",
            outsiderSession.AccessToken));
        foreignDelete.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A club mismatch reads as "not found", not "forbidden".
        var otherClubId = await CreateClubAsync(app, ownerSession.AccessToken, "Unrelated Club");
        var mismatched = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{otherClubId}/discussions/{discussion.Id}",
            memberSession.AccessToken));
        mismatched.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await app.QueryDbAsync(db => db.ClubDiscussions.AnyAsync(d => d.Id == discussion.Id)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task DiscussionEndpoints_ShouldGateReadsOnPrivateClubs()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var ownerSession = await CreateUserSessionAsync(app, "discussions-private-owner@example.com", "Organizer");
        var outsiderSession = await CreateUserSessionAsync(app, "discussions-private-outsider@example.com");

        var clubId = await CreateClubAsync(app, ownerSession.AccessToken, "Private Club", isPrivate: true);

        // The owner counts as staff, so they can post without joining.
        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions",
            ownerSession.AccessToken,
            JsonContent.Create(new { title = "Internal", description = "Members only." })));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var privateDiscussion = (await app.ReadApiResponseAsync<ClubDiscussionResponse>(created)).Data!;

        var anonymous = await app.Client.GetAsync($"/api/clubs/{clubId}/discussions");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var outsider = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{clubId}/discussions",
            outsiderSession.AccessToken));
        outsider.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var asOwner = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{clubId}/discussions",
            ownerSession.AccessToken));
        asOwner.StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.ReadApiResponseAsync<PagedResponse<ClubDiscussionResponse>>(asOwner))
            .Data!.Items.Should().ContainSingle();

        var anonymousReplies = await app.Client.GetAsync(
            $"/api/clubs/{clubId}/discussions/{privateDiscussion.Id}/replies");
        anonymousReplies.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var outsiderReply = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions/{privateDiscussion.Id}/replies",
            outsiderSession.AccessToken,
            JsonContent.Create(new { content = "Not a member" })));
        outsiderReply.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var streamResponse = await app.Client.SendAsync(
            CreateAuthorizedRequest(
                HttpMethod.Get,
                $"/api/clubs/{clubId}/discussions/replies/events",
                ownerSession.AccessToken),
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);
        streamResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        streamResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }

    private static async Task<AuthenticatedSessionResponse> CreateUserSessionAsync(
        AuthApiTestApp app,
        string email,
        string role = "Participant")
    {
        return await app.SignUpAndVerifyByTokenAsync(
            email,
            role: role,
            transport: SessionTransportResolver.ApiValue);
    }

    private static async Task<int> CreateClubAsync(
        AuthApiTestApp app,
        string accessToken,
        string name,
        bool isPrivate = false)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs",
            accessToken,
            JsonContent.Create(new
            {
                Name = name,
                Description = "Campus group",
                Clubtype = "social",
                ClubImageUrl = app.BlobStorage.CreateOwnedBlobUrl("clubs", "club.png"),
                Email = $"{name.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}@example.com",
                IsPrivate = isPrivate
            })));

        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new Xunit.Sdk.XunitException(await app.DescribeFailureAsync(response));
        }

        return (await app.ReadApiResponseAsync<ClubApiModel>(response)).Data!.Id;
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
