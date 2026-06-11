using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.contracts.responses;
using backend.main.features.clubs.follow.contracts.responses;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments.contracts.responses;
using backend.main.features.clubs.posts.contracts.responses;
using backend.main.features.clubs.posts.search;
using backend.main.features.clubs.reviews.contracts.responses;
using backend.main.features.clubs.search;
using backend.main.features.clubs.versions.contracts.responses;
using backend.main.features.events.search;
using backend.main.shared.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

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

        var missing = await app.Client.GetAsync($"/api/clubs/{club.Id}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MembershipEndpoints_ShouldJoinLeaveAndReportMembershipState()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (ownerSession, _) = await CreateUserSessionAsync(app, "member-owner@example.com", "Organizer");
        var (memberSession, _) = await CreateUserSessionAsync(app, "member-user@example.com");

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

        var updatedComment = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/clubs/{club.Id}/posts/{post.Id}/comments/{comment.Id}",
            commenterSession.AccessToken,
            JsonContent.Create(new { content = "Count me in." })));
        updatedComment.StatusCode.Should().Be(HttpStatusCode.OK);

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

        var deletedComment = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/posts/{post.Id}/comments/{comment.Id}",
            commenterSession.AccessToken));
        deletedComment.StatusCode.Should().Be(HttpStatusCode.OK);

        var deletedPost = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/posts/{post.Id}",
            ownerSession.AccessToken));
        deletedPost.StatusCode.Should().Be(HttpStatusCode.OK);
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

        var deletedReview = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/clubs/{club.Id}/reviews/{review.Id}",
            reviewerSession.AccessToken));
        deletedReview.StatusCode.Should().Be(HttpStatusCode.OK);

        var clubAfterDelete = await app.Client.GetAsync($"/api/clubs/{club.Id}");
        var clubAfterDeleteBody = await app.ReadApiResponseAsync<ClubApiModel>(clubAfterDelete);
        clubAfterDeleteBody.Data!.Rating.Should().BeNull();
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
            "/api/admin/clubs/posts?search=Admin%20Visible&page=1&pageSize=20",
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
        response.StatusCode.Should().Be(HttpStatusCode.Created);
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
