using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.clubs.reviews;
using backend.main.features.clubs.search;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class ClubReviewServiceTests
{
    [Fact]
    public async Task CreateReviewAsync_ShouldThrow_WhenClubDoesNotExist()
    {
        await using var harness = await ClubReviewServiceHarness.CreateAsync();
        harness.ClubRepositoryMock
            .Setup(repo => repo.ExistsAsync(harness.ClubId))
            .ReturnsAsync(false);

        var action = () => harness.Service.CreateReviewAsync(harness.ClubId, harness.UserId, "Great", 5, "Nice");

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage($"Club with ID {harness.ClubId} was not found.");
    }

    [Fact]
    public async Task CreateReviewAsync_ShouldCreateReview_UpdateRoundedRating_AndInvalidateCaches()
    {
        await using var harness = await ClubReviewServiceHarness.CreateAsync();
        await harness.SeedClubAsync();
        harness.SetupClubPartialUpdates();

        harness.ClubRepositoryMock
            .Setup(repo => repo.ExistsAsync(harness.ClubId))
            .ReturnsAsync(true);
        harness.ReviewRepositoryMock
            .Setup(repo => repo.CreateAsync(It.IsAny<ClubReview>()))
            .ReturnsAsync((ClubReview review) =>
            {
                review.Id = 18;
                return review;
            });
        harness.ReviewRepositoryMock
            .Setup(repo => repo.GetAverageRatingAsync(harness.ClubId))
            .ReturnsAsync(4.26);

        var created = await harness.Service.CreateReviewAsync(harness.ClubId, harness.UserId, "Great club", 5, "Helpful staff");

        created.Id.Should().Be(18);
        created.ClubId.Should().Be(harness.ClubId);
        created.UserId.Should().Be(harness.UserId);

        var club = await harness.Db.Clubs.SingleAsync(item => item.Id == harness.ClubId);
        club.Rating.Should().Be(4.3);

        harness.OutboxWriterMock.Verify(writer => writer.StageUpsert(It.Is<Club>(club => club.Id == harness.ClubId)), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync($"club:{harness.ClubId}"), Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("clubs:version", 1), Times.Once);
    }

    [Fact]
    public async Task GetReviewsByClubAsync_ShouldThrow_WhenClubDoesNotExist()
    {
        await using var harness = await ClubReviewServiceHarness.CreateAsync();
        harness.ClubRepositoryMock
            .Setup(repo => repo.ExistsAsync(harness.ClubId))
            .ReturnsAsync(false);

        var action = () => harness.Service.GetReviewsByClubAsync(harness.ClubId, 1, 20);

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage($"Club with ID {harness.ClubId} was not found.");
    }

    [Fact]
    public async Task GetReviewsByClubAsync_ShouldReturnRepositoryItems_WhenClubExists()
    {
        await using var harness = await ClubReviewServiceHarness.CreateAsync();
        var expected = new List<ClubReview>
        {
            new() { Id = 1, ClubId = harness.ClubId, UserId = harness.UserId, Title = "Great", Rating = 5 }
        };

        harness.ClubRepositoryMock
            .Setup(repo => repo.ExistsAsync(harness.ClubId))
            .ReturnsAsync(true);
        harness.ReviewRepositoryMock
            .Setup(repo => repo.GetByClubIdAsync(harness.ClubId, 2, 15))
            .ReturnsAsync(expected);

        var reviews = await harness.Service.GetReviewsByClubAsync(harness.ClubId, 2, 15);

        reviews.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task UpdateReviewAsync_ShouldThrow_WhenReviewBelongsToAnotherUser()
    {
        await using var harness = await ClubReviewServiceHarness.CreateAsync();
        harness.ReviewRepositoryMock
            .Setup(repo => repo.GetByIdAsync(30))
            .ReturnsAsync(new ClubReview
            {
                Id = 30,
                ClubId = harness.ClubId,
                UserId = harness.OtherUserId,
                Title = "Existing",
                Rating = 3
            });

        var action = () => harness.Service.UpdateReviewAsync(harness.ClubId, 30, harness.UserId, "Updated", 4, "Changed");

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("You are not allowed to update this review.");
    }

    [Fact]
    public async Task UpdateReviewAsync_ShouldUpdateReview_AndRefreshClubRating()
    {
        await using var harness = await ClubReviewServiceHarness.CreateAsync();
        await harness.SeedClubAsync();
        harness.SetupClubPartialUpdates();

        harness.ReviewRepositoryMock
            .Setup(repo => repo.GetByIdAsync(31))
            .ReturnsAsync(new ClubReview
            {
                Id = 31,
                ClubId = harness.ClubId,
                UserId = harness.UserId,
                Title = "Old",
                Rating = 2
            });
        harness.ReviewRepositoryMock
            .Setup(repo => repo.UpdateAsync(31, It.IsAny<ClubReview>()))
            .ReturnsAsync((int _, ClubReview updated) =>
            {
                updated.Id = 31;
                updated.ClubId = harness.ClubId;
                updated.UserId = harness.UserId;
                return updated;
            });
        harness.ReviewRepositoryMock
            .Setup(repo => repo.GetAverageRatingAsync(harness.ClubId))
            .ReturnsAsync(3.84);

        var updated = await harness.Service.UpdateReviewAsync(harness.ClubId, 31, harness.UserId, "Updated", 4, "Better");

        updated.Title.Should().Be("Updated");
        updated.Rating.Should().Be(4);

        var club = await harness.Db.Clubs.SingleAsync(item => item.Id == harness.ClubId);
        club.Rating.Should().Be(3.8);
        harness.OutboxWriterMock.Verify(writer => writer.StageUpsert(It.Is<Club>(club => club.Id == harness.ClubId)), Times.Once);
    }

    [Fact]
    public async Task DeleteReviewAsync_ShouldThrow_WhenReviewBelongsToAnotherUser()
    {
        await using var harness = await ClubReviewServiceHarness.CreateAsync();
        harness.ReviewRepositoryMock
            .Setup(repo => repo.GetByIdAsync(32))
            .ReturnsAsync(new ClubReview
            {
                Id = 32,
                ClubId = harness.ClubId,
                UserId = harness.OtherUserId,
                Title = "Existing",
                Rating = 3
            });

        var action = () => harness.Service.DeleteReviewAsync(harness.ClubId, 32, harness.UserId);

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("You are not allowed to delete this review.");
    }

    [Fact]
    public async Task DeleteReviewAsync_ShouldDeleteReview_AndClearRating_WhenAverageMissing()
    {
        await using var harness = await ClubReviewServiceHarness.CreateAsync();
        await harness.SeedClubAsync();
        harness.SetupClubPartialUpdates();

        harness.ReviewRepositoryMock
            .Setup(repo => repo.GetByIdAsync(33))
            .ReturnsAsync(new ClubReview
            {
                Id = 33,
                ClubId = harness.ClubId,
                UserId = harness.UserId,
                Title = "Existing",
                Rating = 3
            });
        harness.ReviewRepositoryMock
            .Setup(repo => repo.DeleteAsync(33))
            .ReturnsAsync(true);
        harness.ReviewRepositoryMock
            .Setup(repo => repo.GetAverageRatingAsync(harness.ClubId))
            .ReturnsAsync((double?)null);

        await harness.Service.DeleteReviewAsync(harness.ClubId, 33, harness.UserId);

        var club = await harness.Db.Clubs.SingleAsync(item => item.Id == harness.ClubId);
        club.Rating.Should().BeNull();
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync($"club:{harness.ClubId}"), Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("clubs:version", 1), Times.Once);
    }

    private sealed class ClubReviewServiceHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public ClubReviewService Service { get; }
        public Mock<IClubReviewRepository> ReviewRepositoryMock { get; } = new();
        public Mock<IClubRepository> ClubRepositoryMock { get; } = new();
        public Mock<IUserRepository> UserRepositoryMock { get; } = new();
        public Mock<IClubSearchOutboxWriter> OutboxWriterMock { get; } = new();
        public Mock<ICacheService> CacheMock { get; } = new();

        public int ClubId => 7;
        public int UserId => 11;
        public int OtherUserId => 12;

        private ClubReviewServiceHarness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;

            CacheMock
                .Setup(cache => cache.DeleteKeyAsync(It.IsAny<string>()))
                .ReturnsAsync(true);
            CacheMock
                .Setup(cache => cache.IncrementAsync(It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync(1);

            Service = new ClubReviewService(
                db,
                ReviewRepositoryMock.Object,
                ClubRepositoryMock.Object,
                UserRepositoryMock.Object,
                OutboxWriterMock.Object,
                CacheMock.Object);
        }

        public static async Task<ClubReviewServiceHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();

            db.Users.AddRange(
                new User
                {
                    Id = 11,
                    Email = "owner@test.local",
                    Usertype = "Organizer"
                },
                new User
                {
                    Id = 12,
                    Email = "other@test.local",
                    Usertype = "Participant"
                });
            await db.SaveChangesAsync();

            return new ClubReviewServiceHarness(connection, db);
        }

        public async Task SeedClubAsync()
        {
            Db.Clubs.Add(new Club
            {
                Id = ClubId,
                UserId = UserId,
                Name = "Review Club",
                Description = "A club used for review tests.",
                Clubtype = ClubType.Gaming,
                ClubImage = "https://cdn.test/clubs/review.png",
                Rating = 2.5
            });
            await Db.SaveChangesAsync();
        }

        public void SetupClubPartialUpdates()
        {
            ClubRepositoryMock
                .Setup(repo => repo.UpdatePartialAsync(ClubId, It.IsAny<Action<Club>>()))
                .ReturnsAsync((int clubId, Action<Club> patch) =>
                {
                    var club = Db.Clubs.Single(item => item.Id == clubId);
                    patch(club);
                    return true;
                });
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
