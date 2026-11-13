using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.discussions;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.moderation;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.staff;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

namespace backend.tests.Integration.Application.Security;

/// <summary>
/// End-to-end checks for club moderation (#402): who may change or remove other people's posts,
/// discussions, replies, comments and reviews, and that every moderation action is logged.
/// </summary>
public class ClubModerationEndpointsTests
{
    [Fact]
    public async Task Posts_ShouldBeManagedByTheirAuthorsAndBySeniorStaff_AndPinnedOnlyByManagers()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateSessionAsync(app, "mod-post-owner@example.com", "Organizer");
        var manager = await CreateSessionAsync(app, "mod-post-manager@example.com", "Participant");
        var volunteer = await CreateSessionAsync(app, "mod-post-volunteer@example.com", "Participant");
        var clubId = await CreateClubAsync(app, owner.AccessToken, "Moderated Posts Club");
        var ownerId = await UserIdAsync(app, "mod-post-owner@example.com");
        await app.AddClubStaffAsync(clubId, await UserIdAsync(app, "mod-post-manager@example.com"), ownerId, ClubStaffRole.Manager);
        await app.AddClubStaffAsync(clubId, await UserIdAsync(app, "mod-post-volunteer@example.com"), ownerId, ClubStaffRole.Volunteer);

        var ownerPost = await CreateAsync(app, owner.AccessToken, $"/api/clubs/{clubId}/posts", PostBody("Owner news", pinned: false));

        // A volunteer cannot change or delete a more senior author's post.
        await AssertRefusedAsync(app, volunteer.AccessToken, HttpMethod.Put, $"/api/clubs/{clubId}/posts/{ownerPost}",
            ClubPostService.UpdateForbiddenMessage, PostBody("Hijacked", pinned: false));
        await AssertRefusedAsync(app, volunteer.AccessToken, HttpMethod.Delete,
            $"/api/clubs/{clubId}/posts/{ownerPost}?reason=Outdated", ClubPostService.DeleteForbiddenMessage);

        // A volunteer manages their own post but cannot pin it, even when creating it.
        await AssertRefusedAsync(app, volunteer.AccessToken, HttpMethod.Post, $"/api/clubs/{clubId}/posts",
            ClubPostService.PinForbiddenMessage, PostBody("Look at me", pinned: true));
        var volunteerPost = await CreateAsync(app, volunteer.AccessToken, $"/api/clubs/{clubId}/posts", PostBody("Volunteer news", pinned: false));
        await AssertRefusedAsync(app, volunteer.AccessToken, HttpMethod.Put, $"/api/clubs/{clubId}/posts/{volunteerPost}",
            ClubPostService.PinForbiddenMessage, PostBody("Volunteer news", pinned: true));
        await AssertStatusAsync(app, volunteer.AccessToken, HttpMethod.Put, $"/api/clubs/{clubId}/posts/{volunteerPost}",
            HttpStatusCode.OK, PostBody("Volunteer news, corrected", pinned: false));

        // A manager pins and deletes the owner's post; a removal needs a reason.
        await AssertStatusAsync(app, manager.AccessToken, HttpMethod.Put, $"/api/clubs/{clubId}/posts/{ownerPost}",
            HttpStatusCode.OK, PostBody("Owner news", pinned: true));
        await AssertStatusAsync(app, manager.AccessToken, HttpMethod.Delete, $"/api/clubs/{clubId}/posts/{ownerPost}",
            HttpStatusCode.BadRequest);
        await AssertStatusAsync(app, manager.AccessToken, HttpMethod.Delete,
            $"/api/clubs/{clubId}/posts/{ownerPost}?reason=Superseded", HttpStatusCode.OK);

        // Only the volunteer's own edit is not moderation; the manager's two actions are logged.
        var log = await ReadLogAsync(app, owner.AccessToken, clubId);
        log.Select(entry => entry.Action).Should().Equal(ClubModerationActionTypes.Remove, ClubModerationActionTypes.Pin);
        log.Should().OnlyContain(entry => entry.ContentType == "Post" && entry.ContentId == ownerPost && entry.ActorClubRole == "Manager");
        log[0].Reason.Should().Be("Superseded");

        await AssertRefusedAsync(app, volunteer.AccessToken, HttpMethod.Get, $"/api/clubs/{clubId}/moderation-log",
            ClubModerationLog.ReadForbiddenMessage);
    }

    [Fact]
    public async Task ContentRemoval_ShouldFollowRankInTheirOwnClub_AndLetAdminsModerateAnywhere()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateSessionAsync(app, "mod-rm-owner@example.com", "Organizer");
        var volunteer = await CreateSessionAsync(app, "mod-rm-volunteer@example.com", "Participant");
        var member = await CreateSessionAsync(app, "mod-rm-member@example.com", "Participant");
        var otherOwner = await CreateSessionAsync(app, "mod-rm-other-owner@example.com", "Organizer");
        var otherManager = await CreateSessionAsync(app, "mod-rm-other-manager@example.com", "Participant");
        var admin = await CreateSessionAsync(app, "mod-rm-admin@example.com", "Admin");

        var clubId = await CreateClubAsync(app, owner.AccessToken, "Moderated Content Club");
        var otherClubId = await CreateClubAsync(app, otherOwner.AccessToken, "Neighbouring Club");
        var ownerId = await UserIdAsync(app, "mod-rm-owner@example.com");
        await app.AddClubStaffAsync(clubId, await UserIdAsync(app, "mod-rm-volunteer@example.com"), ownerId, ClubStaffRole.Volunteer);
        await app.AddClubStaffAsync(otherClubId, await UserIdAsync(app, "mod-rm-other-manager@example.com"),
            await UserIdAsync(app, "mod-rm-other-owner@example.com"), ClubStaffRole.Manager);
        await AssertStatusAsync(app, member.AccessToken, HttpMethod.Post, $"/api/clubs/{clubId}/join", HttpStatusCode.OK);

        var post = await CreateAsync(app, owner.AccessToken, $"/api/clubs/{clubId}/posts", PostBody("Welcome", pinned: false));
        var memberComment = await CreateAsync(app, member.AccessToken, $"/api/clubs/{clubId}/posts/{post}/comments",
            new { content = "Buy cheap watches" });
        var ownerDiscussion = await CreateAsync(app, owner.AccessToken, $"/api/clubs/{clubId}/discussions",
            new { title = "House rules", description = "Be kind." });
        var memberReply = await CreateAsync(app, member.AccessToken,
            $"/api/clubs/{clubId}/discussions/{ownerDiscussion}/replies", new { content = "Rude reply", parentReplyId = (int?)null });
        var memberDiscussion = await CreateAsync(app, member.AccessToken, $"/api/clubs/{clubId}/discussions",
            new { title = "Off-topic thread", description = "Spam" });
        var memberReview = await CreateAsync(app, member.AccessToken, $"/api/clubs/{clubId}/reviews",
            new { title = "Terrible", rating = 1, comment = "Abusive review" });

        // Another club's manager has no rank here.
        await AssertRefusedAsync(app, otherManager.AccessToken, HttpMethod.Delete,
            $"/api/clubs/{clubId}/posts/{post}/comments/{memberComment}?reason=Spam", "You are not allowed to delete this comment.");

        // A volunteer removes a member's content, but not the owner's, and needs a reason.
        await AssertStatusAsync(app, volunteer.AccessToken, HttpMethod.Delete,
            $"/api/clubs/{clubId}/posts/{post}/comments/{memberComment}", HttpStatusCode.BadRequest);
        await AssertStatusAsync(app, volunteer.AccessToken, HttpMethod.Delete,
            $"/api/clubs/{clubId}/posts/{post}/comments/{memberComment}?reason=Spam", HttpStatusCode.OK);
        await AssertStatusAsync(app, volunteer.AccessToken, HttpMethod.Delete,
            $"/api/clubs/{clubId}/discussions/{ownerDiscussion}/replies/{memberReply}?reason=Rude", HttpStatusCode.OK);
        await AssertRefusedAsync(app, volunteer.AccessToken, HttpMethod.Delete,
            $"/api/clubs/{clubId}/discussions/{ownerDiscussion}?reason=Spam", "You are not allowed to delete this discussion.");

        // Moderators remove other people's words but do not rewrite them.
        await AssertRefusedAsync(app, volunteer.AccessToken, HttpMethod.Put, $"/api/clubs/{clubId}/discussions/{memberDiscussion}",
            "You are not allowed to update this discussion.", new { title = "Edited", description = "Edited" });

        // Admins moderate in any club, even one they do not belong to.
        await AssertStatusAsync(app, admin.AccessToken, HttpMethod.Delete,
            $"/api/clubs/{clubId}/discussions/{memberDiscussion}?reason=Spam", HttpStatusCode.OK);
        await AssertStatusAsync(app, admin.AccessToken, HttpMethod.Delete,
            $"/api/clubs/{clubId}/reviews/{memberReview}?reason=Abusive", HttpStatusCode.OK);

        var log = await ReadLogAsync(app, owner.AccessToken, clubId);
        log.Select(entry => (entry.ContentType, entry.ActorClubRole)).Should().Equal(
            ("Review", "Admin"),
            ("Discussion", "Admin"),
            ("Reply", "Volunteer"),
            ("Comment", "Volunteer"));
        log.Should().OnlyContain(entry => entry.Action == ClubModerationActionTypes.Remove && entry.Reason != null);
        (await ReadLogAsync(app, otherOwner.AccessToken, otherClubId)).Should().BeEmpty();
    }

    [Fact]
    public async Task RemovedMember_ShouldNotEditOrDeleteTheirDiscussionInAPrivateClub()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateSessionAsync(app, "mod-left-owner@example.com", "Organizer");
        var member = await CreateSessionAsync(app, "mod-left-member@example.com", "Participant");
        var clubId = await CreateClubAsync(app, owner.AccessToken, "Private Moderation Club", isPrivate: true);
        var memberId = await UserIdAsync(app, "mod-left-member@example.com");
        await app.QueryDbAsync(db =>
        {
            db.FollowClubs.Add(new FollowClub { ClubId = clubId, UserId = memberId });
            return db.SaveChangesAsync();
        });

        var discussion = await CreateAsync(app, member.AccessToken, $"/api/clubs/{clubId}/discussions",
            new { title = "My thread", description = "Hello" });
        await AssertStatusAsync(app, member.AccessToken, HttpMethod.Put, $"/api/clubs/{clubId}/discussions/{discussion}",
            HttpStatusCode.OK, new { title = "My thread", description = "Hello again" });

        await AssertStatusAsync(app, member.AccessToken, HttpMethod.Delete, $"/api/clubs/{clubId}/join", HttpStatusCode.OK);

        await AssertRefusedAsync(app, member.AccessToken, HttpMethod.Put, $"/api/clubs/{clubId}/discussions/{discussion}",
            ClubDiscussionService.ContributeForbiddenMessage, new { title = "Still mine", description = "Edited after leaving" });
        await AssertRefusedAsync(app, member.AccessToken, HttpMethod.Delete, $"/api/clubs/{clubId}/discussions/{discussion}",
            ClubDiscussionService.ContributeForbiddenMessage);
    }

    private static object PostBody(string title, bool pinned) => new
    {
        title,
        content = "Post body",
        postType = PostType.General,
        isPinned = pinned,
    };

    private static async Task<int> UserIdAsync(AuthApiTestApp app, string email) =>
        (await app.FindUserByEmailAsync(email))!.Id;

    private static async Task<int> CreateAsync(AuthApiTestApp app, string accessToken, string path, object body)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(HttpMethod.Post, path, accessToken, JsonContent.Create(body)));
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"POST {path}: {await app.DescribeFailureAsync(response)}");
        return (await app.ReadApiResponseAsync<IdModel>(response)).Data!.Id;
    }

    private static async Task<List<LogEntry>> ReadLogAsync(AuthApiTestApp app, string accessToken, int clubId)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get, $"/api/clubs/{clubId}/moderation-log", accessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => new LogEntry(
                item.GetProperty("action").GetString()!,
                item.GetProperty("contentType").GetString()!,
                item.GetProperty("contentId").GetInt32(),
                item.GetProperty("actorClubRole").GetString()!,
                item.GetProperty("reason").GetString()))
            .ToList();
    }

    private static async Task AssertRefusedAsync(
        AuthApiTestApp app, string accessToken, HttpMethod method, string path, string message, object? body = null)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            method, path, accessToken, body is null ? null : JsonContent.Create(body)));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {path}: {await app.DescribeFailureAsync(response)}");

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("message").GetString().Should().Be(message);
        payload.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("FORBIDDEN");
    }

    private static async Task AssertStatusAsync(
        AuthApiTestApp app, string accessToken, HttpMethod method, string path, HttpStatusCode expected, object? body = null)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            method, path, accessToken, body is null ? null : JsonContent.Create(body)));
        response.StatusCode.Should().Be(expected, $"{method} {path}: {await app.DescribeFailureAsync(response)}");
    }

    private static async Task<AuthenticatedSessionResponse> CreateSessionAsync(AuthApiTestApp app, string email, string role)
    {
        if (role != "Admin")
            return await app.SignUpAndVerifyByTokenAsync(email, role: role, transport: SessionTransportResolver.ApiValue);

        // Admin cannot self-register, so it is seeded and logged in, once per test: the shared
        // client keeps the trusted-device cookie from that login.
        var user = await app.SeedUserAsync(email, role: role);
        var device = $"{user.Username}-device";
        await app.SeedKnownDeviceAsync(user.Id, device);
        return await app.LoginApiAsync(user.Username!, trustedDeviceToken: device);
    }

    private static async Task<int> CreateClubAsync(AuthApiTestApp app, string accessToken, string name, bool isPrivate = false)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs",
            accessToken,
            JsonContent.Create(new
            {
                Name = name,
                Description = "Moderation club",
                Clubtype = "social",
                ClubImageUrl = await app.CreateClubImageUrlAsync(accessToken),
                Email = $"{name.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()}@example.com",
                IsPrivate = isPrivate
            })));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(response));
        await app.ReindexClubsAsync();

        return (await app.ReadApiResponseAsync<IdModel>(response)).Data!.Id;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method, string path, string accessToken, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = content;
        return request;
    }

    private sealed record LogEntry(string Action, string ContentType, int ContentId, string ActorClubRole, string? Reason);

    private sealed class IdModel
    {
        public int Id { get; set; }
    }
}
