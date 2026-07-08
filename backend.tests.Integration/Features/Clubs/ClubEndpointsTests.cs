using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs;
using backend.main.features.clubs.contracts.responses;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.follow.contracts.responses;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments;
using backend.main.features.clubs.posts.comments.contracts.responses;
using backend.main.features.clubs.posts.contracts.responses;
using backend.main.features.clubs.posts.search;
using backend.main.features.clubs.reviews;
using backend.main.features.clubs.reviews.contracts.responses;
using backend.main.features.clubs.search;
using backend.main.features.clubs.staff;
using backend.main.features.clubs.versions;
using backend.main.features.clubs.versions.contracts.responses;
using backend.main.features.events.search;
using backend.main.infrastructure.database.core;
using backend.main.shared.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Integration.Features.Clubs;

public class ClubEndpointsTests
{
    [Fact]
    public async Task ClubLifecycle_ShouldCreateUpdateFetchManagedAndDeleteClub()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "clubs-owner@example.com", "Organizer");

        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs",
            ownerSession.AccessToken,
            JsonContent.Create(CreateClubPayload(
                app,
                name: "Chess Club",
                description: "Board games",
                clubtype: "social",
                email: "chess@example.com"))));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await app.ReadApiResponseAsync<ClubApiModel>(created);
        var club = createdBody.Data!;
        club.Name.Should().Be("Chess Club");
        club.IsOwner.Should().BeTrue();
        club.CanManage.Should().BeTrue();
        club.ClubImage.Should().StartWith("https://storage.test/event-assets/clubs/");

        var persistedClub = await app.QueryDbAsync(db => db.Clubs.SingleOrDefaultAsync(c => c.Id == club.Id));
        persistedClub.Should().NotBeNull();
        persistedClub!.Name.Should().Be("Chess Club");
        persistedClub.Email.Should().Be("chess@example.com");
        persistedClub.UserId.Should().Be(club.OwnerId);

        var fetched = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}",
            ownerSession.AccessToken));
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetchedBody = await app.ReadApiResponseAsync<ClubApiModel>(fetched);
        fetchedBody.Data!.OwnerId.Should().Be(club.OwnerId);

        var updated = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{club.Id}",
            ownerSession.AccessToken,
            JsonContent.Create(CreateClubPayload(
                app,
                name: "Campus Chess Club",
                description: "Board nights",
                clubtype: "social",
                email: "campus-chess@example.com"))));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedBody = await app.ReadApiResponseAsync<ClubApiModel>(updated);
        updatedBody.Data!.Name.Should().Be("Campus Chess Club");
        updatedBody.Data.Email.Should().Be("campus-chess@example.com");

        var persistedAfterUpdate = await app.QueryDbAsync(db => db.Clubs.SingleAsync(c => c.Id == club.Id));
        persistedAfterUpdate.Name.Should().Be("Campus Chess Club");
        persistedAfterUpdate.Email.Should().Be("campus-chess@example.com");
        persistedAfterUpdate.CurrentVersionNumber.Should().BeGreaterThan(persistedClub.CurrentVersionNumber);

        var managed = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/clubs/managed",
            ownerSession.AccessToken));
        managed.StatusCode.Should().Be(HttpStatusCode.OK);
        var managedBody = await app.ReadApiResponseAsync<IEnumerable<ClubApiModel>>(managed);
        managedBody.Data.Should().ContainSingle(entry => entry.Id == club.Id);

        var deleted = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}",
            ownerSession.AccessToken));
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.QueryDbAsync(db => db.Clubs.AnyAsync(c => c.Id == club.Id))).Should().BeFalse();

        var missing = await app.Client.GetAsync($"/api/clubs/{club.Id}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MembershipEndpoints_ShouldJoinLeaveAndReportMembershipState()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "member-owner@example.com", "Organizer");
        var (memberSession, member) = await CreateUserSessionAsync(app, "member-user@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Robotics Club");

        var beforeJoin = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/members/me",
            memberSession.AccessToken));
        beforeJoin.StatusCode.Should().Be(HttpStatusCode.OK);
        (await beforeJoin.Content.ReadAsStringAsync()).Should().Contain("\"isMember\":false");

        var joined = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/join",
            memberSession.AccessToken));
        joined.StatusCode.Should().Be(HttpStatusCode.OK);

        var membership = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/members/me",
            memberSession.AccessToken));
        membership.StatusCode.Should().Be(HttpStatusCode.OK);
        (await membership.Content.ReadAsStringAsync()).Should().Contain("\"isMember\":true");

        (await app.QueryDbAsync(db =>
            db.FollowClubs.AnyAsync(f => f.ClubId == club.Id && f.UserId == member!.Id)))
            .Should().BeTrue();
        (await app.QueryDbAsync(db => db.Clubs.Where(c => c.Id == club.Id).Select(c => c.MemberCount).SingleAsync()))
            .Should().Be(1);

        var members = await app.Client.GetAsync($"/api/clubs/{club.Id}/members");
        members.StatusCode.Should().Be(HttpStatusCode.OK);
        var membersBody = await app.ReadApiResponseAsync<IEnumerable<FollowResponse>>(members);
        membersBody.Data.Should().ContainSingle(entry => entry.ClubId == club.Id);

        var left = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/join",
            memberSession.AccessToken));
        left.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterLeave = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/members/me",
            memberSession.AccessToken));
        afterLeave.StatusCode.Should().Be(HttpStatusCode.OK);
        (await afterLeave.Content.ReadAsStringAsync()).Should().Contain("\"isMember\":false");

        (await app.QueryDbAsync(db =>
            db.FollowClubs.AnyAsync(f => f.ClubId == club.Id && f.UserId == member!.Id)))
            .Should().BeFalse();
        (await app.QueryDbAsync(db => db.Clubs.Where(c => c.Id == club.Id).Select(c => c.MemberCount).SingleAsync()))
            .Should().Be(0);
    }

    [Fact]
    public async Task StaffEndpoints_ShouldAddRemoveAndTransferOwnership()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "staff-owner@example.com", "Organizer");
        var (managerSession, manager) = await CreateUserSessionAsync(app, "staff-manager@example.com");
        var (newOwnerSession, newOwner) = await CreateUserSessionAsync(app, "staff-new-owner@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Film Club");

        var addManager = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/staff/managers",
            ownerSession.AccessToken,
            JsonContent.Create(new { userId = manager!.Id })));
        addManager.StatusCode.Should().Be(HttpStatusCode.Created);

        var persistedManager = await app.QueryDbAsync(db =>
            db.ClubStaff.SingleOrDefaultAsync(s => s.ClubId == club.Id && s.UserId == manager!.Id));
        persistedManager.Should().NotBeNull();
        persistedManager!.Role.Should().Be(ClubStaffRole.Manager);

        var staff = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/staff",
            ownerSession.AccessToken));
        staff.StatusCode.Should().Be(HttpStatusCode.OK);
        (await staff.Content.ReadAsStringAsync()).Should().Contain($"\"userId\":{manager.Id}");

        var removed = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/staff/{manager.Id}",
            ownerSession.AccessToken));
        removed.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.QueryDbAsync(db =>
            db.ClubStaff.AnyAsync(s => s.ClubId == club.Id && s.UserId == manager.Id)))
            .Should().BeFalse();

        var managedByFormerManager = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/clubs/managed",
            managerSession.AccessToken));
        managedByFormerManager.StatusCode.Should().Be(HttpStatusCode.OK);
        var managedByFormerManagerBody = await app.ReadApiResponseAsync<IEnumerable<ClubApiModel>>(managedByFormerManager);
        managedByFormerManagerBody.Data.Should().BeEmpty();

        var transferred = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/transfer-ownership",
            ownerSession.AccessToken,
            JsonContent.Create(new { newOwnerUserId = newOwner!.Id })));
        transferred.StatusCode.Should().Be(HttpStatusCode.OK);

        var transferredBody = await app.ReadApiResponseAsync<ClubApiModel>(transferred);
        transferredBody.Data!.OwnerId.Should().Be(newOwner.Id);
        transferredBody.Data.IsOwner.Should().BeFalse();

        (await app.QueryDbAsync(db => db.Clubs.Where(c => c.Id == club.Id).Select(c => c.UserId).SingleAsync()))
            .Should().Be(newOwner.Id);

        var managedByNewOwner = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/clubs/managed",
            newOwnerSession.AccessToken));
        managedByNewOwner.StatusCode.Should().Be(HttpStatusCode.OK);
        var newOwnerManaged = await app.ReadApiResponseAsync<IEnumerable<ClubApiModel>>(managedByNewOwner);
        newOwnerManaged.Data.Should().ContainSingle(entry => entry.Id == club.Id && entry.IsOwner);

        var fetchedByNewOwner = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}",
            newOwnerSession.AccessToken));
        fetchedByNewOwner.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetchedByNewOwnerBody = await app.ReadApiResponseAsync<ClubApiModel>(fetchedByNewOwner);
        fetchedByNewOwnerBody.Data!.OwnerId.Should().Be(newOwner.Id);
        fetchedByNewOwnerBody.Data.IsOwner.Should().BeTrue();
    }

    [Fact]
    public async Task PostAndCommentEndpoints_ShouldSupportCrudFlows()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "posts-owner@example.com", "Organizer");
        var (commenterSession, _) = await CreateUserSessionAsync(app, "posts-commenter@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Gaming Club");

        var createdPost = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/posts",
            ownerSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Launch Night",
                content = "Bring your favorite game.",
                postType = PostType.Announcement,
                isPinned = true
            })));
        createdPost.StatusCode.Should().Be(HttpStatusCode.Created);
        var postBody = await app.ReadApiResponseAsync<ClubPostResponse>(createdPost);
        var post = postBody.Data!;

        var persistedPost = await app.QueryDbAsync(db => db.ClubPosts.SingleOrDefaultAsync(p => p.Id == post.Id));
        persistedPost.Should().NotBeNull();
        persistedPost!.ClubId.Should().Be(club.Id);
        persistedPost.Title.Should().Be("Launch Night");
        persistedPost.Content.Should().Be("Bring your favorite game.");
        persistedPost.PostType.Should().Be(PostType.Announcement);
        persistedPost.IsPinned.Should().BeTrue();

        var posts = await app.Client.GetAsync($"/api/clubs/{club.Id}/posts");
        posts.StatusCode.Should().Be(HttpStatusCode.OK);
        var postsBody = await app.ReadApiResponseAsync<PagedResponse<ClubPostResponse>>(posts);
        postsBody.Data!.Items.Should().ContainSingle(entry => entry.Id == post.Id);

        var createdComment = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/posts/{post.Id}/comments",
            commenterSession.AccessToken,
            JsonContent.Create(new { content = "I am in." })));
        createdComment.StatusCode.Should().Be(HttpStatusCode.Created);
        var commentBody = await app.ReadApiResponseAsync<PostCommentResponse>(createdComment);
        var comment = commentBody.Data!;

        var persistedComment = await app.QueryDbAsync(db => db.PostComments.SingleOrDefaultAsync(c => c.Id == comment.Id));
        persistedComment.Should().NotBeNull();
        persistedComment!.PostId.Should().Be(post.Id);
        persistedComment.Content.Should().Be("I am in.");

        var updatedComment = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{club.Id}/posts/{post.Id}/comments/{comment.Id}",
            commenterSession.AccessToken,
            JsonContent.Create(new { content = "Count me in." })));
        updatedComment.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.QueryDbAsync(db => db.PostComments.Where(c => c.Id == comment.Id).Select(c => c.Content).SingleAsync()))
            .Should().Be("Count me in.");

        var comments = await app.Client.GetAsync($"/api/clubs/{club.Id}/posts/{post.Id}/comments");
        comments.StatusCode.Should().Be(HttpStatusCode.OK);
        var commentsBody = await app.ReadApiResponseAsync<PagedResponse<PostCommentResponse>>(comments);
        commentsBody.Data!.Items.Should().ContainSingle(entry => entry.Content == "Count me in.");

        var updatedPost = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{club.Id}/posts/{post.Id}",
            ownerSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Launch Night Updated",
                content = "Bring two games.",
                postType = PostType.General,
                isPinned = false
            })));
        updatedPost.StatusCode.Should().Be(HttpStatusCode.OK);

        var persistedUpdatedPost = await app.QueryDbAsync(db => db.ClubPosts.SingleAsync(p => p.Id == post.Id));
        persistedUpdatedPost.Title.Should().Be("Launch Night Updated");
        persistedUpdatedPost.Content.Should().Be("Bring two games.");
        persistedUpdatedPost.PostType.Should().Be(PostType.General);
        persistedUpdatedPost.IsPinned.Should().BeFalse();

        var deletedComment = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/posts/{post.Id}/comments/{comment.Id}",
            commenterSession.AccessToken));
        deletedComment.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.QueryDbAsync(db => db.PostComments.AnyAsync(c => c.Id == comment.Id))).Should().BeFalse();

        var deletedPost = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/posts/{post.Id}",
            ownerSession.AccessToken));
        deletedPost.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.QueryDbAsync(db => db.ClubPosts.AnyAsync(p => p.Id == post.Id))).Should().BeFalse();
    }

    [Fact]
    public async Task PostDetailAndCommentStreamEndpoints_ShouldFetchSinglePostAndStartEventStream()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "posts-detail-owner@example.com", "Organizer");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Readers Club");

        var createdPost = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/posts",
            ownerSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Detailed Post",
                content = "A single-post detail integration test body.",
                postType = PostType.General,
                isPinned = false
            })));
        createdPost.StatusCode.Should().Be(HttpStatusCode.Created);
        var post = (await app.ReadApiResponseAsync<ClubPostResponse>(createdPost)).Data!;

        var detail = await app.Client.GetAsync($"/api/clubs/{club.Id}/posts/{post.Id}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailBody = await app.ReadApiResponseAsync<ClubPostResponse>(detail);
        detailBody.Data!.Id.Should().Be(post.Id);
        detailBody.Data.Title.Should().Be("Detailed Post");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var streamRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/posts/{post.Id}/comments/events");
        using var streamResponse = await app.Client.SendAsync(
            streamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        streamResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        streamResponse.Content.Headers.ContentType.Should().NotBeNull();
        streamResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        await using var stream = await streamResponse.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var firstLine = await reader.ReadLineAsync();
        firstLine.Should().Be(": keepalive");
    }

    [Fact]
    public async Task ReviewEndpoints_ShouldSupportCrudAndUpdateClubRating()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "reviews-owner@example.com", "Organizer");
        var (reviewerSession, _) = await CreateUserSessionAsync(app, "reviews-user@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Music Club");

        var createdReview = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/reviews",
            reviewerSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Great rehearsals",
                rating = 5,
                comment = "Very welcoming."
            })));
        createdReview.StatusCode.Should().Be(HttpStatusCode.Created);
        var reviewBody = await app.ReadApiResponseAsync<ClubReviewResponse>(createdReview);
        var review = reviewBody.Data!;

        var reviews = await app.Client.GetAsync($"/api/clubs/{club.Id}/reviews");
        reviews.StatusCode.Should().Be(HttpStatusCode.OK);
        var reviewsBody = await app.ReadApiResponseAsync<IEnumerable<ClubReviewResponse>>(reviews);
        reviewsBody.Data.Should().ContainSingle(entry => entry.Id == review.Id);

        var clubAfterCreate = await app.Client.GetAsync($"/api/clubs/{club.Id}");
        var clubAfterCreateBody = await app.ReadApiResponseAsync<ClubApiModel>(clubAfterCreate);
        clubAfterCreateBody.Data!.Rating.Should().Be(5.0);

        var persistedReview = await app.QueryDbAsync(db => db.ClubReviews.SingleOrDefaultAsync(r => r.Id == review.Id));
        persistedReview.Should().NotBeNull();
        persistedReview!.ClubId.Should().Be(club.Id);
        persistedReview.Rating.Should().Be(5);
        (await app.QueryDbAsync(db => db.Clubs.Where(c => c.Id == club.Id).Select(c => c.Rating).SingleAsync()))
            .Should().Be(5.0);

        var updatedReview = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{club.Id}/reviews/{review.Id}",
            reviewerSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Good rehearsals",
                rating = 3,
                comment = "Still solid."
            })));
        updatedReview.StatusCode.Should().Be(HttpStatusCode.OK);

        var clubAfterUpdate = await app.Client.GetAsync($"/api/clubs/{club.Id}");
        var clubAfterUpdateBody = await app.ReadApiResponseAsync<ClubApiModel>(clubAfterUpdate);
        clubAfterUpdateBody.Data!.Rating.Should().Be(3.0);

        (await app.QueryDbAsync(db => db.ClubReviews.Where(r => r.Id == review.Id).Select(r => r.Rating).SingleAsync()))
            .Should().Be(3);
        (await app.QueryDbAsync(db => db.Clubs.Where(c => c.Id == club.Id).Select(c => c.Rating).SingleAsync()))
            .Should().Be(3.0);

        var deletedReview = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/reviews/{review.Id}",
            reviewerSession.AccessToken));
        deletedReview.StatusCode.Should().Be(HttpStatusCode.OK);

        var clubAfterDelete = await app.Client.GetAsync($"/api/clubs/{club.Id}");
        var clubAfterDeleteBody = await app.ReadApiResponseAsync<ClubApiModel>(clubAfterDelete);
        clubAfterDeleteBody.Data!.Rating.Should().BeNull();

        (await app.QueryDbAsync(db => db.ClubReviews.AnyAsync(r => r.Id == review.Id))).Should().BeFalse();
        (await app.QueryDbAsync(db => db.Clubs.Where(c => c.Id == club.Id).Select(c => c.Rating).SingleAsync()))
            .Should().BeNull();
    }

    [Fact]
    public async Task ClubContentAndAdminEndpoints_ShouldReturnForbidden_ForUnauthorizedActors()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "club-content-owner@example.com", "Organizer");
        var (commenterSession, _) = await CreateUserSessionAsync(app, "club-content-commenter@example.com");
        var (reviewerSession, reviewer) = await CreateUserSessionAsync(app, "club-content-reviewer@example.com");
        var (outsiderSession, _) = await CreateUserSessionAsync(app, "club-content-outsider@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Club Content Auth");

        var createdPost = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/posts",
            ownerSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Protected Post",
                content = "Only club managers should mutate this post.",
                postType = PostType.Announcement,
                isPinned = false
            })));
        createdPost.StatusCode.Should().Be(HttpStatusCode.Created);
        var post = (await app.ReadApiResponseAsync<ClubPostResponse>(createdPost)).Data!;

        var createdComment = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/posts/{post.Id}/comments",
            commenterSession.AccessToken,
            JsonContent.Create(new { content = "My protected comment." })));
        createdComment.StatusCode.Should().Be(HttpStatusCode.Created);
        var comment = (await app.ReadApiResponseAsync<PostCommentResponse>(createdComment)).Data!;

        var createdReview = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/reviews",
            reviewerSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Protected Review",
                rating = 4,
                comment = "Only the author should mutate this review."
            })));
        createdReview.StatusCode.Should().Be(HttpStatusCode.Created);
        var review = (await app.ReadApiResponseAsync<ClubReviewResponse>(createdReview)).Data!;

        var createPost = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/posts",
            outsiderSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Outsider Post",
                content = "An outsider should not be able to create this post.",
                postType = PostType.General,
                isPinned = false
            })));
        createPost.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var updatePost = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{club.Id}/posts/{post.Id}",
            outsiderSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Outsider Update",
                content = "Blocked outsider post update.",
                postType = PostType.General,
                isPinned = false
            })));
        updatePost.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deletePost = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/posts/{post.Id}",
            outsiderSession.AccessToken));
        deletePost.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var updateComment = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{club.Id}/posts/{post.Id}/comments/{comment.Id}",
            outsiderSession.AccessToken,
            JsonContent.Create(new { content = "Outsider edit attempt." })));
        updateComment.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deleteComment = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/posts/{post.Id}/comments/{comment.Id}",
            outsiderSession.AccessToken));
        deleteComment.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var updateReview = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{club.Id}/reviews/{review.Id}",
            outsiderSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Outsider Review Update",
                rating = 2,
                comment = "Blocked outsider review update."
            })));
        updateReview.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deleteReview = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/reviews/{review.Id}",
            outsiderSession.AccessToken));
        deleteReview.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        reviewer.Should().NotBeNull();

        var userReviews = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/users/{reviewer.Id}/reviews?page=1&pageSize=20",
            outsiderSession.AccessToken));
        userReviews.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var adminPosts = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/admin/clubs/posts?page=1&pageSize=20",
            outsiderSession.AccessToken));
        adminPosts.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var adminPostReindex = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/admin/clubs/posts/reindex",
            outsiderSession.AccessToken));
        adminPostReindex.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var adminClubReindex = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/admin/clubs/reindex",
            outsiderSession.AccessToken));
        adminClubReindex.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ClubContentEndpoints_ShouldReturnNotFound_WhenRouteIdsDoNotMatchEntities()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "club-mismatch-owner@example.com", "Organizer");
        var (commenterSession, _) = await CreateUserSessionAsync(app, "club-mismatch-commenter@example.com");
        var (reviewerSession, _) = await CreateUserSessionAsync(app, "club-mismatch-reviewer@example.com");

        var primaryClub = await CreateClubAsync(app, ownerSession.AccessToken, "Primary Club");
        var otherClub = await CreateClubAsync(app, ownerSession.AccessToken, "Other Club");

        var createdPost = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{primaryClub.Id}/posts",
            ownerSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Primary Post",
                content = "A post used to validate route/entity mismatch handling.",
                postType = PostType.General,
                isPinned = false
            })));
        createdPost.StatusCode.Should().Be(HttpStatusCode.Created);
        var post = (await app.ReadApiResponseAsync<ClubPostResponse>(createdPost)).Data!;

        var createdComment = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{primaryClub.Id}/posts/{post.Id}/comments",
            commenterSession.AccessToken,
            JsonContent.Create(new { content = "Route mismatch comment." })));
        createdComment.StatusCode.Should().Be(HttpStatusCode.Created);
        var comment = (await app.ReadApiResponseAsync<PostCommentResponse>(createdComment)).Data!;

        var createdReview = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{primaryClub.Id}/reviews",
            reviewerSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Primary Review",
                rating = 5,
                comment = "Route mismatch review."
            })));
        createdReview.StatusCode.Should().Be(HttpStatusCode.Created);
        var review = (await app.ReadApiResponseAsync<ClubReviewResponse>(createdReview)).Data!;

        var postDetailMismatch = await app.Client.GetAsync($"/api/clubs/{otherClub.Id}/posts/{post.Id}");
        postDetailMismatch.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var commentListMismatch = await app.Client.GetAsync($"/api/clubs/{otherClub.Id}/posts/{post.Id}/comments");
        commentListMismatch.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var commentUpdateMismatch = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{primaryClub.Id}/posts/{post.Id + 999}/comments/{comment.Id}",
            commenterSession.AccessToken,
            JsonContent.Create(new { content = "Should not resolve." })));
        commentUpdateMismatch.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var reviewUpdateMismatch = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{otherClub.Id}/reviews/{review.Id}",
            reviewerSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Wrong Club",
                rating = 2,
                comment = "Should not resolve."
            })));
        reviewUpdateMismatch.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var reviewDeleteMismatch = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{otherClub.Id}/reviews/{review.Id}",
            reviewerSession.AccessToken));
        reviewDeleteMismatch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DiscoveryEndpoints_ShouldListAndSearchClubs()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "clubs-discovery-owner@example.com", "Organizer");

        var robotics = await CreateClubAsync(app, ownerSession.AccessToken, "Astro Robotics");
        var debateResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs",
            ownerSession.AccessToken,
            JsonContent.Create(CreateClubPayload(
                app,
                name: "Debate Society",
                description: "Campus debate",
                clubtype: "academic",
                email: "debate@example.com"))));
        debateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var debate = (await app.ReadApiResponseAsync<ClubApiModel>(debateResponse)).Data!;
        await app.ReindexClubsAsync();

        var list = await app.Client.GetAsync("/api/clubs?search=robotics&clubType=social&page=1&pageSize=20");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await app.ReadApiResponseAsync<PagedResponse<ClubApiModel>>(list);
        listBody.Data!.Items.Should().ContainSingle(item => item.Id == robotics.Id);
        listBody.Data.Items.Should().NotContain(item => item.Id == debate.Id);

        var search = await app.Client.PostAsJsonAsync("/api/clubs/search", new
        {
            query = "Debate",
            page = 1,
            pageSize = 20
        });
        search.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchBody = await app.ReadApiResponseAsync<PagedResponse<ClubApiModel>>(search);
        searchBody.Data!.Items.Should().ContainSingle(item => item.Id == debate.Id);
        searchBody.Data.Items.Should().NotContain(item => item.Id == robotics.Id);
    }

    [Fact]
    public async Task VolunteerAndVersionEndpoints_ShouldAddVolunteerAndSupportRollback()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "clubs-version-owner@example.com", "Organizer");
        var (volunteerSession, volunteer) = await CreateUserSessionAsync(app, "clubs-version-volunteer@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Service Club");

        var addVolunteer = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/staff/volunteers",
            ownerSession.AccessToken,
            JsonContent.Create(new { userId = volunteer!.Id })));
        addVolunteer.StatusCode.Should().Be(HttpStatusCode.Created);
        var addVolunteerBody = await app.ReadApiResponseAsync<ClubStaffResponse>(addVolunteer);
        addVolunteerBody.Data!.UserId.Should().Be(volunteer.Id);
        addVolunteerBody.Data.Role.Should().Be("Volunteer");

        var persistedVolunteer = await app.QueryDbAsync(db =>
            db.ClubStaff.SingleOrDefaultAsync(s => s.ClubId == club.Id && s.UserId == volunteer.Id));
        persistedVolunteer.Should().NotBeNull();
        persistedVolunteer!.Role.Should().Be(ClubStaffRole.Volunteer);

        var volunteerManaged = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/clubs/managed",
            volunteerSession.AccessToken));
        volunteerManaged.StatusCode.Should().Be(HttpStatusCode.OK);
        var volunteerManagedBody = await app.ReadApiResponseAsync<IEnumerable<ClubApiModel>>(volunteerManaged);
        volunteerManagedBody.Data.Should().ContainSingle(item => item.Id == club.Id);

        var updated = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{club.Id}",
            ownerSession.AccessToken,
            JsonContent.Create(CreateClubPayload(
                app,
                name: "Service Club Updated",
                description: "Community group",
                clubtype: "social",
                email: "service-updated@example.com"))));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var persistedActionTypes = await app.QueryDbAsync(db =>
            db.ClubVersions.Where(v => v.ClubId == club.Id).Select(v => v.ActionType).ToListAsync());
        persistedActionTypes.Should().Contain("create");
        persistedActionTypes.Should().Contain("update");

        var versions = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/versions?page=0&pageSize=200",
            ownerSession.AccessToken));
        versions.StatusCode.Should().Be(HttpStatusCode.OK);
        var versionsBody = await app.ReadApiResponseAsync<PagedResponse<ClubVersionListItemResponse>>(versions);
        versionsBody.Data!.Page.Should().Be(1);
        versionsBody.Data.PageSize.Should().Be(100);
        versionsBody.Data.Items.Should().HaveCountGreaterThanOrEqualTo(2);
        versionsBody.Data.Items.Should().Contain(item => item.ActionType == "create");
        versionsBody.Data.Items.Should().Contain(item => item.ActionType == "update");

        var versionDetail = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/versions/1",
            ownerSession.AccessToken));
        versionDetail.StatusCode.Should().Be(HttpStatusCode.OK);
        var versionDetailBody = await app.ReadApiResponseAsync<ClubVersionDetailResponse>(versionDetail);
        versionDetailBody.Data!.VersionNumber.Should().Be(1);
        versionDetailBody.Data.Snapshot.Name.Should().Be("Service Club");
        versionDetailBody.Data.Snapshot.Email.Should().Be("service-club@example.com");

        var rollback = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/versions/1/rollback",
            ownerSession.AccessToken,
            JsonContent.Create(new { })));
        rollback.StatusCode.Should().Be(HttpStatusCode.OK);
        var rollbackBody = await app.ReadApiResponseAsync<ClubRollbackApiModel>(rollback);
        rollbackBody.Data!.Club.Name.Should().Be("Service Club");
        rollbackBody.Data.Club.Email.Should().Be("service-club@example.com");
        rollbackBody.Data.RestoredFromVersionNumber.Should().Be(1);

        var persistedAfterRollback = await app.QueryDbAsync(db => db.Clubs.SingleAsync(c => c.Id == club.Id));
        persistedAfterRollback.Name.Should().Be("Service Club");
        persistedAfterRollback.Email.Should().Be("service-club@example.com");
        persistedAfterRollback.CurrentVersionNumber.Should().Be(rollbackBody.Data.NewVersionNumber);

        var fetched = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}",
            volunteerSession.AccessToken));
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetchedBody = await app.ReadApiResponseAsync<ClubApiModel>(fetched);
        fetchedBody.Data!.Name.Should().Be("Service Club");
        fetchedBody.Data.IsVolunteer.Should().BeTrue();
        fetchedBody.Data.CanManage.Should().BeFalse();
    }

    [Fact]
    public async Task ClubMutationEndpoints_ShouldRejectDuplicateAndInvalidMembershipStaffAndOwnershipChanges()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, owner) = await CreateUserSessionAsync(app, "clubs-negative-owner@example.com", "Organizer");
        var (memberSession, member) = await CreateUserSessionAsync(app, "clubs-negative-member@example.com");
        var (_, manager) = await CreateUserSessionAsync(app, "clubs-negative-manager@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Negative Club");

        var firstJoin = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/join",
            memberSession.AccessToken));
        firstJoin.StatusCode.Should().Be(HttpStatusCode.OK);

        var duplicateJoin = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/join",
            memberSession.AccessToken));
        duplicateJoin.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await app.QueryDbAsync(db => db.FollowClubs.CountAsync(f => f.ClubId == club.Id && f.UserId == member!.Id)))
            .Should().Be(1);

        var firstLeave = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/join",
            memberSession.AccessToken));
        firstLeave.StatusCode.Should().Be(HttpStatusCode.OK);

        var duplicateLeave = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/join",
            memberSession.AccessToken));
        duplicateLeave.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var addManager = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/staff/managers",
            ownerSession.AccessToken,
            JsonContent.Create(new { userId = manager!.Id })));
        addManager.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicateManager = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/staff/managers",
            ownerSession.AccessToken,
            JsonContent.Create(new { userId = manager.Id })));
        duplicateManager.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await app.QueryDbAsync(db => db.ClubStaff.CountAsync(s => s.ClubId == club.Id && s.UserId == manager.Id)))
            .Should().Be(1);

        var ownerAsManager = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/staff/managers",
            ownerSession.AccessToken,
            JsonContent.Create(new { userId = owner!.Id })));
        ownerAsManager.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var removeOwner = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/staff/{owner.Id}",
            ownerSession.AccessToken));
        removeOwner.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var transferToCurrentOwner = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/transfer-ownership",
            ownerSession.AccessToken,
            JsonContent.Create(new { newOwnerUserId = owner.Id })));
        transferToCurrentOwner.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var rollbackCurrentVersion = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/versions/{club.CurrentVersionNumber}/rollback",
            ownerSession.AccessToken,
            JsonContent.Create(new { })));
        rollbackCurrentVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ClubManagementEndpoints_ShouldReturnForbidden_ForOutsiders()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "clubs-authz-owner@example.com", "Organizer");
        var (outsiderSession, outsider) = await CreateUserSessionAsync(app, "clubs-authz-outsider@example.com");
        var (_, targetUser) = await CreateUserSessionAsync(app, "clubs-authz-target@example.com");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Protected Club");

        var update = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{club.Id}",
            outsiderSession.AccessToken,
            JsonContent.Create(new
            {
                name = "Outsider Update",
                description = "Blocked update",
                clubtype = "social",
                clubImageUrl = club.ClubImage,
                email = "outsider-update@example.com"
            })));
        update.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var delete = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}",
            outsiderSession.AccessToken));
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var staff = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/staff",
            outsiderSession.AccessToken));
        staff.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var addManager = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/staff/managers",
            outsiderSession.AccessToken,
            JsonContent.Create(new { userId = targetUser!.Id })));
        addManager.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var addVolunteer = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/staff/volunteers",
            outsiderSession.AccessToken,
            JsonContent.Create(new { userId = targetUser.Id })));
        addVolunteer.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var removeStaff = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/staff/{outsider!.Id}",
            outsiderSession.AccessToken));
        removeStaff.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var transferOwnership = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/transfer-ownership",
            outsiderSession.AccessToken,
            JsonContent.Create(new { newOwnerUserId = targetUser.Id })));
        transferOwnership.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var versions = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/versions",
            outsiderSession.AccessToken));
        versions.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var versionDetail = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/clubs/{club.Id}/versions/1",
            outsiderSession.AccessToken));
        versionDetail.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var rollback = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/versions/1/rollback",
            outsiderSession.AccessToken,
            JsonContent.Create(new { })));
        rollback.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ClubDiscoveryEndpoints_ShouldRejectInvalidPaging()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "clubs-discovery-invalid-owner@example.com", "Organizer");

        await CreateClubAsync(app, ownerSession.AccessToken, "Paging Club");

        var invalidListPage = await app.Client.GetAsync("/api/clubs?page=0&pageSize=20");
        invalidListPage.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidListPageSize = await app.Client.GetAsync("/api/clubs?page=1&pageSize=101");
        invalidListPageSize.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidSearch = await app.Client.PostAsJsonAsync("/api/clubs/search", new
        {
            query = "Paging",
            page = 0,
            pageSize = 101
        });
        invalidSearch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UserScopedFollowAndReviewEndpoints_ShouldListFollowedClubsAndUserReviews()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "user-scope-owner@example.com", "Organizer");
        var (memberSession, member) = await CreateUserSessionAsync(app, "user-scope-member@example.com");
        var (reviewerSession, reviewer) = await CreateUserSessionAsync(app, "user-scope-reviewer@example.com");

        var admin = await app.SeedUserAsync("user-scope-admin@example.com", role: "Admin");
        await app.SeedKnownDeviceAsync(admin.Id, "user-scope-admin-device");
        var adminSession = await app.LoginApiAsync("user-scope-admin@example.com", trustedDeviceToken: "user-scope-admin-device");

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Scope Club");

        var joined = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/join",
            memberSession.AccessToken));
        joined.StatusCode.Should().Be(HttpStatusCode.OK);

        var followed = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/users/{member!.Id}/clubs/following?page=1&pageSize=20",
            memberSession.AccessToken));
        followed.StatusCode.Should().Be(HttpStatusCode.OK);
        var followedBody = await app.ReadApiResponseAsync<IEnumerable<FollowResponse>>(followed);
        followedBody.Data.Should().ContainSingle(item => item.ClubId == club.Id && item.UserId == member.Id);

        var createdReview = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/reviews",
            reviewerSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Scoped Review",
                rating = 4,
                comment = "Worth listing for the reviewing user."
            })));
        createdReview.StatusCode.Should().Be(HttpStatusCode.Created);
        var review = (await app.ReadApiResponseAsync<ClubReviewResponse>(createdReview)).Data!;

        var userReviews = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/users/{reviewer!.Id}/reviews?page=1&pageSize=20",
            adminSession.AccessToken));
        userReviews.StatusCode.Should().Be(HttpStatusCode.OK);
        var userReviewsBody = await app.ReadApiResponseAsync<IEnumerable<ClubReviewResponse>>(userReviews);
        userReviewsBody.Data.Should().ContainSingle(item => item.Id == review.Id && item.UserId == reviewer.Id);
    }

    [Fact]
    public async Task AdminPostAndReindexEndpoints_ShouldListPostsAndReturnIndexedCounts()
    {
        var postReindex = new FixedClubPostReindexService(7);
        var clubReindex = new FixedClubReindexService(11);
        var eventReindex = new FixedEventReindexService(13);

        await using var app = await AuthApiTestApp.CreateAsync(services =>
        {
            services.AddSingleton<IClubPostReindexService>(postReindex);
            services.AddSingleton<IClubReindexService>(clubReindex);
            services.AddSingleton<IEventReindexService>(eventReindex);
        });

        var admin = await app.SeedUserAsync("clubs-admin@example.com", role: "Admin");
        await app.SeedKnownDeviceAsync(admin.Id, "clubs-admin-device");
        var adminSession = await app.LoginApiAsync("clubs-admin@example.com", trustedDeviceToken: "clubs-admin-device");

        var (ownerSession, _) = await CreateUserSessionAsync(app, "clubs-admin-owner@example.com", "Organizer");
        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Admin Posts Club");

        var createdPost = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/clubs/{club.Id}/posts",
            ownerSession.AccessToken,
            JsonContent.Create(new
            {
                title = "Admin Visible Post",
                content = "This post should appear in the admin feed.",
                postType = PostType.Announcement,
                isPinned = true
            })));
        createdPost.StatusCode.Should().Be(HttpStatusCode.Created);
        var post = (await app.ReadApiResponseAsync<ClubPostResponse>(createdPost)).Data!;

        var allPosts = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/admin/clubs/posts?page=1&pageSize=20",
            adminSession.AccessToken));
        allPosts.StatusCode.Should().Be(HttpStatusCode.OK);
        var allPostsBody = await app.ReadApiResponseAsync<PagedResponse<ClubPostResponse>>(allPosts);
        allPostsBody.Data!.Items.Should().ContainSingle(item => item.Id == post.Id);

        var reindexPosts = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/admin/clubs/posts/reindex",
            adminSession.AccessToken));
        reindexPosts.StatusCode.Should().Be(HttpStatusCode.OK);
        var reindexPostsBody = await app.ReadApiResponseAsync<IndexedCountResponse>(reindexPosts);
        reindexPostsBody.Data!.Indexed.Should().Be(7);

        var reindexClubs = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/admin/clubs/reindex",
            adminSession.AccessToken));
        reindexClubs.StatusCode.Should().Be(HttpStatusCode.OK);
        var reindexClubsBody = await app.ReadApiResponseAsync<IndexedCountResponse>(reindexClubs);
        reindexClubsBody.Data!.Indexed.Should().Be(11);

        var reindexEvents = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/admin/events/reindex",
            adminSession.AccessToken));
        reindexEvents.StatusCode.Should().Be(HttpStatusCode.OK);
        var reindexEventsBody = await app.ReadApiResponseAsync<IndexedCountResponse>(reindexEvents);
        reindexEventsBody.Data!.Indexed.Should().Be(13);
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

    private static async Task<ClubApiModel> CreateClubAsync(
        AuthApiTestApp app,
        string accessToken,
        string name)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs",
            accessToken,
            JsonContent.Create(CreateClubPayload(
                app,
                name: name,
                description: "Campus group",
                clubtype: "social",
                email: $"{name.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}@example.com"))));
        var diagnostics = await app.DescribeFailureAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.Created, diagnostics);
        await app.ReindexClubsAsync();
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

    private static object CreateClubPayload(
        AuthApiTestApp app,
        string name,
        string description,
        string clubtype,
        string? email = null)
    {
        return new
        {
            Name = name,
            Description = description,
            Clubtype = clubtype,
            ClubImageUrl = app.BlobStorage.CreateOwnedBlobUrl("clubs", "club.png"),
            Email = email
        };
    }

    private sealed class ClubApiModel
    {
        public int Id { get; init; }
        public int OwnerId { get; init; }
        public int CurrentVersionNumber { get; init; }
        public string Name { get; init; } = string.Empty;
        public string ClubImage { get; init; } = string.Empty;
        public string? Email { get; init; }
        public double? Rating { get; init; }
        public bool IsOwner { get; init; }
        public bool CanManage { get; init; }
        public bool IsVolunteer { get; init; }
    }

    private sealed class ClubRollbackApiModel
    {
        public ClubApiModel Club { get; init; } = new();
        public int RestoredFromVersionNumber { get; init; }
        public int NewVersionNumber { get; init; }
    }

    private sealed class IndexedCountResponse
    {
        public int Indexed { get; init; }
    }

    private sealed class FixedClubPostReindexService(int count) : IClubPostReindexService
    {
        public Task<int> ReindexAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(count);
    }

    private sealed class FixedClubReindexService(int count) : IClubReindexService
    {
        public Task<int> ReindexAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(count);
    }

    private sealed class FixedEventReindexService(int count) : IEventReindexService
    {
        public Task<int> ReindexAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(count);
    }

}




