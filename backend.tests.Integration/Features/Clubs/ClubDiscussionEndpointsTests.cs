using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.application.bootstrap;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.discussions.contracts.responses;
using backend.main.features.clubs.discussions.replies.contracts.responses;
using backend.main.features.clubs.realtime;
using backend.main.features.clubs.realtime.contracts.responses;
using backend.main.shared.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
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
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies/{reply.Id}/reaction",
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
            $"/api/clubs/{clubId}/discussions/{discussion.Id}/replies/{reply.Id}/reaction",
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // The owner can subscribe to the private club over the realtime hub...
        await using var ownerHub = app.CreateHubConnection(
            RoutePaths.ClubRealtimeHubPath, ownerSession.AccessToken);
        await ownerHub.StartAsync(cts.Token);
        await ownerHub.InvokeAsync(nameof(ClubRealtimeHub.JoinClub), clubId, cts.Token);

        // ...while a non-member is refused by the same gate the REST endpoints use.
        await using var outsiderHub = app.CreateHubConnection(
            RoutePaths.ClubRealtimeHubPath, outsiderSession.AccessToken);
        await outsiderHub.StartAsync(cts.Token);
        var outsiderJoin = async () =>
            await outsiderHub.InvokeAsync(nameof(ClubRealtimeHub.JoinClub), clubId, cts.Token);
        (await outsiderJoin.Should().ThrowAsync<HubException>())
            .Which.Message.Should().Contain("member of this club");
    }

    [Fact]
    public async Task RealtimeHub_ShouldReportPresenceAndTypingToOtherMembers()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateUserSessionAsync(app, "presence-owner@example.com", "Organizer");
        var member = await CreateUserSessionAsync(app, "presence-member@example.com");
        var clubId = await CreateClubAsync(app, owner.AccessToken, "Presence Club");

        var discussionResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions",
            owner.AccessToken,
            JsonContent.Create(new { title = "Presence topic", description = "Who is here?" })));
        var discussion = (await app.ReadApiResponseAsync<ClubDiscussionResponse>(discussionResponse)).Data!;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var ownerHub = app.CreateHubConnection(
            RoutePaths.ClubRealtimeHubPath, owner.AccessToken);
        var presenceChanges = new List<PresenceDiff>();
        var presenceSignal = new SemaphoreSlim(0);
        ownerHub.On<PresenceDiff>(ClubRealtimeEvents.PresenceChanged, diff =>
        {
            lock (presenceChanges)
                presenceChanges.Add(diff);
            presenceSignal.Release();
        });

        var typingSnapshots = new List<ThreadTypingSnapshot>();
        var typingSignal = new SemaphoreSlim(0);
        ownerHub.On<ThreadTypingSnapshot>(ClubRealtimeEvents.TypingChanged, snapshot =>
        {
            lock (typingSnapshots)
                typingSnapshots.Add(snapshot);
            typingSignal.Release();
        });

        await ownerHub.StartAsync(cts.Token);
        await ownerHub.InvokeAsync(nameof(ClubRealtimeHub.JoinClub), clubId, cts.Token);
        await ownerHub.InvokeAsync(
            nameof(ClubRealtimeHub.JoinDiscussion), clubId, discussion.Id, cts.Token);

        // The owner's own JoinDiscussion replies with an initial (empty) typing snapshot.
        (await typingSignal.WaitAsync(TimeSpan.FromSeconds(10), cts.Token)).Should().BeTrue();

        await using var memberHub = app.CreateHubConnection(
            RoutePaths.ClubRealtimeHubPath, member.AccessToken);
        await memberHub.StartAsync(cts.Token);
        await memberHub.InvokeAsync(nameof(ClubRealtimeHub.JoinClub), clubId, cts.Token);

        (await presenceSignal.WaitAsync(TimeSpan.FromSeconds(10), cts.Token)).Should().BeTrue();
        lock (presenceChanges)
        {
            var joined = presenceChanges.Should().ContainSingle().Subject;
            joined.Joined.Should().NotBeNull();
            joined.Joined!.Username.Should().NotBeNullOrEmpty();
            joined.LeftUserId.Should().BeNull();
            joined.TotalOnline.Should().Be(2);
        }

        await memberHub.InvokeAsync(
            nameof(ClubRealtimeHub.JoinDiscussion), clubId, discussion.Id, cts.Token);
        await memberHub.InvokeAsync(
            nameof(ClubRealtimeHub.Typing), ClubRealtimeGroups.DiscussionKind, discussion.Id, true, cts.Token);

        (await typingSignal.WaitAsync(TimeSpan.FromSeconds(10), cts.Token)).Should().BeTrue();
        lock (typingSnapshots)
        {
            typingSnapshots[^1].Users.Should().ContainSingle()
                .Which.Username.Should().NotBeNullOrEmpty();
        }

        // A client whose roster has drained can ask for the full list again.
        var resent = new TaskCompletionSource<PresenceSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ownerHub.On<PresenceSnapshot>(
            ClubRealtimeEvents.PresenceSnapshot, snapshot => resent.TrySetResult(snapshot));

        await ownerHub.InvokeAsync(nameof(ClubRealtimeHub.RequestPresence), clubId, cts.Token);

        var snapshotAgain = await resent.Task.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);
        snapshotAgain.TotalOnline.Should().Be(2);
        snapshotAgain.Users.Should().HaveCount(2);

        // Disconnecting the member takes them out of the roster.
        await memberHub.StopAsync(cts.Token);

        (await presenceSignal.WaitAsync(TimeSpan.FromSeconds(10), cts.Token)).Should().BeTrue();
        lock (presenceChanges)
        {
            var left = presenceChanges[^1];
            left.Joined.Should().BeNull();
            left.LeftUserId.Should().NotBeNull();
            left.TotalOnline.Should().Be(1);
        }
    }

    [Fact]
    public async Task RealtimeHub_ShouldRefuseADiscussionThatBelongsToAnotherClub()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateUserSessionAsync(app, "cross-club-owner@example.com", "Organizer");
        var outsider = await CreateUserSessionAsync(app, "cross-club-outsider@example.com");

        var publicClubId = await CreateClubAsync(app, owner.AccessToken, "Cross Public Club");
        var privateClubId = await CreateClubAsync(
            app, owner.AccessToken, "Cross Private Club", isPrivate: true);

        var privateDiscussionResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{privateClubId}/discussions",
            owner.AccessToken,
            JsonContent.Create(new { title = "Private topic", description = "Members only." })));
        var privateDiscussion =
            (await app.ReadApiResponseAsync<ClubDiscussionResponse>(privateDiscussionResponse)).Data!;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await using var hub = app.CreateHubConnection(
            RoutePaths.ClubRealtimeHubPath, outsider.AccessToken);
        await hub.StartAsync(cts.Token);

        // The typing group is keyed on the discussion alone, so pairing a readable club with a
        // discussion from a private one must not get the caller into that group.
        var crossClubJoin = async () => await hub.InvokeAsync(
            nameof(ClubRealtimeHub.JoinDiscussion), publicClubId, privateDiscussion.Id, cts.Token);

        await crossClubJoin.Should().ThrowAsync<HubException>();

        var crossClubTyping = async () => await hub.InvokeAsync(
            nameof(ClubRealtimeHub.Typing),
            ClubRealtimeGroups.DiscussionKind,
            privateDiscussion.Id,
            true,
            cts.Token);

        (await crossClubTyping.Should().ThrowAsync<HubException>())
            .Which.Message.Should().Contain("Join the thread");
    }

    [Fact]
    public async Task RealtimeHub_ShouldRejectTypingFromAnonymousCallersAndUnjoinedThreads()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateUserSessionAsync(app, "typing-owner@example.com", "Organizer");
        var clubId = await CreateClubAsync(app, owner.AccessToken, "Typing Club");

        var discussionResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{clubId}/discussions",
            owner.AccessToken,
            JsonContent.Create(new { title = "Typing topic", description = "Rules." })));
        var discussion = (await app.ReadApiResponseAsync<ClubDiscussionResponse>(discussionResponse)).Data!;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Anonymous callers may read a public club but must not broadcast typing.
        await using var anonymousHub = app.CreateHubConnection(RoutePaths.ClubRealtimeHubPath);
        await anonymousHub.StartAsync(cts.Token);
        await anonymousHub.InvokeAsync(nameof(ClubRealtimeHub.JoinClub), clubId, cts.Token);
        await anonymousHub.InvokeAsync(
            nameof(ClubRealtimeHub.JoinDiscussion), clubId, discussion.Id, cts.Token);

        var anonymousTyping = async () => await anonymousHub.InvokeAsync(
            nameof(ClubRealtimeHub.Typing), ClubRealtimeGroups.DiscussionKind, discussion.Id, true, cts.Token);
        (await anonymousTyping.Should().ThrowAsync<HubException>())
            .Which.Message.Should().Contain("Authentication is required");

        // An authenticated caller still has to join the thread first.
        await using var ownerHub = app.CreateHubConnection(
            RoutePaths.ClubRealtimeHubPath, owner.AccessToken);
        await ownerHub.StartAsync(cts.Token);

        var unjoinedTyping = async () => await ownerHub.InvokeAsync(
            nameof(ClubRealtimeHub.Typing), ClubRealtimeGroups.DiscussionKind, discussion.Id, true, cts.Token);
        (await unjoinedTyping.Should().ThrowAsync<HubException>())
            .Which.Message.Should().Contain("Join the thread");
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
