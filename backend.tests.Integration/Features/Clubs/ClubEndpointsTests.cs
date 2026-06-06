using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.follow.contracts.responses;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments.contracts.responses;
using backend.main.features.clubs.posts.contracts.responses;
using backend.main.features.clubs.reviews.contracts.responses;
using backend.main.shared.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

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
    }

}
