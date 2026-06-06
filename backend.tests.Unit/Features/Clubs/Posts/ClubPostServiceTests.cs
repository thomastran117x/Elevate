using System.Text.Json;

using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.search;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.infrastructure.database.core;
using backend.main.infrastructure.elasticsearch;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

namespace backend.tests.Unit.Features.Clubs.Posts;

public class ClubPostServiceTests
{
    [Fact]
    public async Task CreateAsync_ShouldThrow_WhenUserCannotManagePosts()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        harness.ClubServiceMock
            .Setup(service => service.CanManageClubPostsAsync(harness.ClubId, harness.UserId, harness.UserRole))
            .ReturnsAsync(false);

        var action = () => harness.Service.CreateAsync(
            harness.ClubId,
            harness.UserId,
            harness.UserRole,
            "Title",
            "Body",
            PostType.Announcement,
            true);

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("You are not allowed to create posts for this club.");
    }

    [Fact]
    public async Task CreateAsync_ShouldCreatePost_AndStageUpsert()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        harness.PostRepositoryMock
            .Setup(repo => repo.CreateAsync(It.IsAny<ClubPost>()))
            .ReturnsAsync((ClubPost post) =>
            {
                post.Id = harness.PostId;
                return post;
            });

        var created = await harness.Service.CreateAsync(
            harness.ClubId,
            harness.UserId,
            harness.UserRole,
            "Launch post",
            "Welcome everyone",
            PostType.Announcement,
            true);

        created.Id.Should().Be(harness.PostId);
        created.ClubId.Should().Be(harness.ClubId);
        created.UserId.Should().Be(harness.UserId);
        created.Title.Should().Be("Launch post");
        created.Content.Should().Be("Welcome everyone");
        created.PostType.Should().Be(PostType.Announcement);
        created.IsPinned.Should().BeTrue();

        harness.OutboxWriterMock.Verify(writer => writer.StageUpsert(It.Is<ClubPost>(post =>
            post.Id == harness.PostId &&
            post.ClubId == harness.ClubId &&
            post.UserId == harness.UserId)), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldRequireAuthentication_ForPrivateClub()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync(isPrivate: true);

        var action = () => harness.Service.GetByIdAsync(harness.ClubId, harness.PostId, null, null);

        await action.Should()
            .ThrowAsync<UnauthorizedException>()
            .WithMessage("Authentication is required to view posts for a private club.");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldRequireMembership_WhenRequesterLacksPrivateClubAccess()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync(isPrivate: true);
        harness.ClubServiceMock
            .Setup(service => service.HasClubStaffAccessAsync(harness.ClubId, harness.UserId, harness.UserRole))
            .ReturnsAsync(false);
        harness.FollowRepositoryMock
            .Setup(repo => repo.IsFollowingClubAsync(harness.ClubId, harness.UserId))
            .ReturnsAsync((FollowClub?)null);

        var action = () => harness.Service.GetByIdAsync(harness.ClubId, harness.PostId, harness.UserId, harness.UserRole);

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("You must be a member of this club to view its posts.");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldThrow_WhenPostBelongsToDifferentClub()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        harness.PostRepositoryMock
            .Setup(repo => repo.GetByIdAsync(harness.PostId))
            .ReturnsAsync(harness.BuildPost(clubId: harness.OtherClubId));

        var action = () => harness.Service.GetByIdAsync(harness.ClubId, harness.PostId, harness.UserId, harness.UserRole);

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage($"Post with ID {harness.PostId} was not found.");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnPostAndAuthor_FromCacheFactory()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        var post = harness.BuildPost();
        harness.PostRepositoryMock
            .Setup(repo => repo.GetByIdAsync(harness.PostId))
            .ReturnsAsync(post);

        var response = await harness.Service.GetByIdAsync(
            harness.ClubId,
            harness.PostId,
            harness.UserId,
            harness.UserRole);

        var result = response.Post;
        var author = response.Author;

        result.Should().BeEquivalentTo(post);
        author.Should().NotBeNull();
        author!.Id.Should().Be(harness.UserId);
        author.Username.Should().Be("owner");
        harness.CacheMock.Verify(cache => cache.GetOrSetAsync(
            $"post:{harness.PostId}",
            It.IsAny<Func<Task<ClubPost?>>>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<double>(),
            It.IsAny<JsonSerializerOptions?>()), Times.Once);
    }

    [Fact]
    public async Task GetByClubIdAsync_ShouldRequireAuthentication_ForPrivateClub()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync(isPrivate: true);

        var action = () => harness.Service.GetByClubIdAsync(
            harness.ClubId,
            null,
            null,
            null,
            PostSortBy.Recent,
            1,
            10);

        await action.Should()
            .ThrowAsync<UnauthorizedException>()
            .WithMessage("Authentication is required to view posts for a private club.");
    }

    [Fact]
    public async Task GetByClubIdAsync_ShouldUseElasticsearch_WhenSearchSucceeds()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        var posts = new List<ClubPost>
        {
            harness.BuildPost(postId: harness.PostId, userId: harness.UserId, title: "One"),
            harness.BuildPost(postId: harness.SecondPostId, userId: harness.SecondUserId, title: "Two")
        };

        harness.SearchServiceMock
            .Setup(service => service.SearchByClubAsync(harness.ClubId, "welcome", PostSortBy.Popular, 2, 5))
            .ReturnsAsync(([harness.PostId, harness.SecondPostId], 9));
        harness.PostRepositoryMock
            .Setup(repo => repo.GetByIdsAsync(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { harness.PostId, harness.SecondPostId }))))
            .ReturnsAsync(posts);

        var result = await harness.Service.GetByClubIdAsync(
            harness.ClubId,
            harness.UserId,
            harness.UserRole,
            "welcome",
            PostSortBy.Popular,
            2,
            5);

        result.Items.Should().BeEquivalentTo(posts);
        result.TotalCount.Should().Be(9);
        result.Source.Should().Be(ResponseSource.Elasticsearch);
        result.Authors.Keys.Should().BeEquivalentTo(new[] { harness.UserId, harness.SecondUserId });
        harness.PostRepositoryMock.Verify(repo => repo.IncrementViewCountAsync(
            It.Is<IEnumerable<int>>(ids => ids.OrderBy(id => id).SequenceEqual(new[] { harness.PostId, harness.SecondPostId }.OrderBy(id => id)))), Times.Once);
    }

    [Fact]
    public async Task GetByClubIdAsync_ShouldFallBackToDatabase_WhenElasticsearchIsDisabled()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        var posts = new List<ClubPost> { harness.BuildPost() };

        harness.SearchServiceMock
            .Setup(service => service.SearchByClubAsync(harness.ClubId, "term", PostSortBy.Recent, 1, 10))
            .ThrowsAsync(new ElasticsearchDisabledException("disabled"));
        harness.PostRepositoryMock
            .Setup(repo => repo.GetByClubIdAsync(harness.ClubId, "term", PostSortBy.Recent, 1, 10))
            .ReturnsAsync(posts);
        harness.PostRepositoryMock
            .Setup(repo => repo.CountByClubIdAsync(harness.ClubId, "term"))
            .ReturnsAsync(4);

        var result = await harness.Service.GetByClubIdAsync(
            harness.ClubId,
            harness.UserId,
            harness.UserRole,
            "term",
            PostSortBy.Recent,
            1,
            10);

        result.Items.Should().BeEquivalentTo(posts);
        result.TotalCount.Should().Be(4);
        result.Source.Should().Be(ResponseSource.Database);
        result.Authors.Keys.Should().ContainSingle().Which.Should().Be(harness.UserId);
        harness.PostRepositoryMock.Verify(repo => repo.IncrementViewCountAsync(
            It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { harness.PostId }))), Times.Once);
    }

    [Fact]
    public async Task GetByClubIdAsync_ShouldRethrowUnexpectedSearchErrors()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        harness.SearchServiceMock
            .Setup(service => service.SearchByClubAsync(harness.ClubId, "boom", PostSortBy.Recent, 1, 10))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var action = () => harness.Service.GetByClubIdAsync(
            harness.ClubId,
            harness.UserId,
            harness.UserRole,
            "boom",
            PostSortBy.Recent,
            1,
            10);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenPostBelongsToDifferentClub()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        harness.PostRepositoryMock
            .Setup(repo => repo.GetByIdAsync(harness.PostId))
            .ReturnsAsync(harness.BuildPost(clubId: harness.OtherClubId));

        var action = () => harness.Service.UpdateAsync(
            harness.ClubId,
            harness.PostId,
            harness.UserId,
            harness.UserRole,
            "Updated",
            "Updated body",
            PostType.Event,
            true);

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage($"Post with ID {harness.PostId} was not found.");
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenUserCannotManagePosts()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        harness.PostRepositoryMock
            .Setup(repo => repo.GetByIdAsync(harness.PostId))
            .ReturnsAsync(harness.BuildPost());
        harness.ClubServiceMock
            .Setup(service => service.CanManageClubPostsAsync(harness.ClubId, harness.UserId, harness.UserRole))
            .ReturnsAsync(false);

        var action = () => harness.Service.UpdateAsync(
            harness.ClubId,
            harness.PostId,
            harness.UserId,
            harness.UserRole,
            "Updated",
            "Updated body",
            PostType.Event,
            true);

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("You are not allowed to update this post.");
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdatePost_StageUpsert_AndInvalidateCache()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        var existing = harness.BuildPost();
        var updated = harness.BuildPost(title: "Updated", content: "Edited", postType: PostType.Event, isPinned: true);

        harness.PostRepositoryMock
            .Setup(repo => repo.GetByIdAsync(harness.PostId))
            .ReturnsAsync(existing);
        harness.PostRepositoryMock
            .Setup(repo => repo.UpdateAsync(harness.PostId, It.IsAny<ClubPost>()))
            .ReturnsAsync(updated);

        var result = await harness.Service.UpdateAsync(
            harness.ClubId,
            harness.PostId,
            harness.UserId,
            harness.UserRole,
            "Updated",
            "Edited",
            PostType.Event,
            true);

        result.Should().BeEquivalentTo(updated);
        harness.OutboxWriterMock.Verify(writer => writer.StageUpsert(It.Is<ClubPost>(post =>
            post.Id == harness.PostId &&
            post.Title == "Updated")), Times.Once);
        harness.CacheMock.Verify(cache => cache.RemoveAsync($"post:{harness.PostId}"), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrow_WhenUserCannotManagePosts()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        harness.PostRepositoryMock
            .Setup(repo => repo.GetByIdAsync(harness.PostId))
            .ReturnsAsync(harness.BuildPost());
        harness.ClubServiceMock
            .Setup(service => service.CanManageClubPostsAsync(harness.ClubId, harness.UserId, harness.UserRole))
            .ReturnsAsync(false);

        var action = () => harness.Service.DeleteAsync(
            harness.ClubId,
            harness.PostId,
            harness.UserId,
            harness.UserRole);

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("You are not allowed to delete this post.");
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeletePost_StageDelete_AndInvalidateCache()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        harness.PostRepositoryMock
            .Setup(repo => repo.GetByIdAsync(harness.PostId))
            .ReturnsAsync(harness.BuildPost());
        harness.PostRepositoryMock
            .Setup(repo => repo.DeleteAsync(harness.PostId))
            .ReturnsAsync(true);

        await harness.Service.DeleteAsync(
            harness.ClubId,
            harness.PostId,
            harness.UserId,
            harness.UserRole);

        harness.OutboxWriterMock.Verify(writer => writer.StageDelete(harness.PostId), Times.Once);
        harness.CacheMock.Verify(cache => cache.RemoveAsync($"post:{harness.PostId}"), Times.Once);
    }

    [Fact]
    public async Task GetAllAdminAsync_ShouldUseElasticsearch_WhenSearchSucceeds()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        var posts = new List<ClubPost> { harness.BuildPost(), harness.BuildPost(postId: harness.SecondPostId, title: "Later") };

        harness.SearchServiceMock
            .Setup(service => service.SearchAllAsync("guide", PostSortBy.Popular, 3, 2))
            .ReturnsAsync(([harness.PostId, harness.SecondPostId], 14));
        harness.PostRepositoryMock
            .Setup(repo => repo.GetByIdsAsync(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { harness.PostId, harness.SecondPostId }))))
            .ReturnsAsync(posts);

        var result = await harness.Service.GetAllAdminAsync("guide", PostSortBy.Popular, 3, 2);

        result.Items.Should().BeEquivalentTo(posts);
        result.TotalCount.Should().Be(14);
        result.Source.Should().Be(ResponseSource.Elasticsearch);
    }

    [Fact]
    public async Task GetAllAdminAsync_ShouldFallBackToDatabase_WhenElasticsearchIsUnavailable()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        var posts = new List<ClubPost> { harness.BuildPost() };

        harness.SearchServiceMock
            .Setup(service => service.SearchAllAsync("ops", PostSortBy.Recent, 1, 25))
            .ThrowsAsync(new ElasticsearchUnavailableException("down"));
        harness.PostRepositoryMock
            .Setup(repo => repo.GetAllAsync("ops", PostSortBy.Recent, 1, 25))
            .ReturnsAsync(posts);
        harness.PostRepositoryMock
            .Setup(repo => repo.CountAllAsync("ops"))
            .ReturnsAsync(6);

        var result = await harness.Service.GetAllAdminAsync("ops", PostSortBy.Recent, 1, 25);

        result.Items.Should().BeEquivalentTo(posts);
        result.TotalCount.Should().Be(6);
        result.Source.Should().Be(ResponseSource.Database);
    }

    [Fact]
    public async Task GetAllAdminAsync_ShouldRethrowUnexpectedSearchErrors()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync();
        harness.SearchServiceMock
            .Setup(service => service.SearchAllAsync("boom", PostSortBy.Recent, 1, 10))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var action = () => harness.Service.GetAllAdminAsync("boom", PostSortBy.Recent, 1, 10);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");
    }

    private sealed class ClubPostServiceHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly Dictionary<int, UserListRecord> _users = new();

        public int ClubId => 4;
        public int OtherClubId => 11;
        public int UserId => 7;
        public int SecondUserId => 21;
        public int PostId => 15;
        public int SecondPostId => 19;

        public string UserRole => "Organizer";

        public AppDatabaseContext Db { get; }
        public Mock<IClubPostRepository> PostRepositoryMock { get; } = new();
        public Mock<IClubService> ClubServiceMock { get; } = new();
        public Mock<IFollowRepository> FollowRepositoryMock { get; } = new();
        public Mock<IClubPostSearchService> SearchServiceMock { get; } = new();
        public Mock<IClubPostSearchOutboxWriter> OutboxWriterMock { get; } = new();
        public Mock<IUserRepository> UserRepositoryMock { get; } = new();
        public Mock<IRefreshAheadCache> CacheMock { get; } = new();
        public ClubPostService Service { get; }

        private ClubPostServiceHarness(SqliteConnection connection, AppDatabaseContext db, bool isPrivate)
        {
            _connection = connection;
            Db = db;

            var club = BuildClub(isPrivate);

            _users[UserId] = new UserListRecord
            {
                Id = UserId,
                Email = "owner@example.com",
                Username = "owner",
                Usertype = "Organizer"
            };
            _users[SecondUserId] = new UserListRecord
            {
                Id = SecondUserId,
                Email = "member@example.com",
                Username = "member",
                Usertype = "Participant"
            };

            ClubServiceMock
                .Setup(service => service.GetClub(ClubId))
                .ReturnsAsync(club);
            ClubServiceMock
                .Setup(service => service.CanManageClubPostsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>()))
                .ReturnsAsync(true);
            ClubServiceMock
                .Setup(service => service.HasClubStaffAccessAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>()))
                .ReturnsAsync(false);

            FollowRepositoryMock
                .Setup(repo => repo.IsFollowingClubAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new FollowClub { ClubId = ClubId, UserId = UserId });

            CacheMock
                .Setup(cache => cache.GetOrSetAsync(
                    It.IsAny<string>(),
                    It.IsAny<Func<Task<ClubPost?>>>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<double>(),
                    It.IsAny<JsonSerializerOptions?>()))
                .Returns((string key, Func<Task<ClubPost?>> factory, TimeSpan ttl, TimeSpan? nullTtl, double threshold, JsonSerializerOptions? options) => factory());
            CacheMock
                .Setup(cache => cache.RemoveAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            UserRepositoryMock
                .Setup(repo => repo.GetByIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<UserReadDetailLevel>()))
                .ReturnsAsync((IEnumerable<int> ids, UserReadDetailLevel _) =>
                    ids.Where(_users.ContainsKey).Select(id => _users[id]).ToList());

            Service = new ClubPostService(
                db,
                PostRepositoryMock.Object,
                ClubServiceMock.Object,
                FollowRepositoryMock.Object,
                SearchServiceMock.Object,
                OutboxWriterMock.Object,
                UserRepositoryMock.Object,
                CacheMock.Object);
        }

        public static async Task<ClubPostServiceHarness> CreateAsync(bool isPrivate = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();

            return new ClubPostServiceHarness(connection, db, isPrivate);
        }

        public ClubPost BuildPost(
            int postId = 15,
            int clubId = 4,
            int userId = 7,
            string title = "Post title",
            string content = "Post content",
            PostType postType = PostType.General,
            bool isPinned = false)
        {
            return new ClubPost
            {
                Id = postId,
                ClubId = clubId,
                UserId = userId,
                Title = title,
                Content = content,
                PostType = postType,
                IsPinned = isPinned,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private Club BuildClub(bool isPrivate)
        {
            return new Club
            {
                Id = ClubId,
                Name = "Chess Club",
                Description = "Weekly chess nights",
                Clubtype = ClubType.Gaming,
                ClubImage = "/images/chess.png",
                UserId = UserId,
                isPrivate = isPrivate
            };
        }
    }
}
