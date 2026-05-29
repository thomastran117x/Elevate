using backend.main.features.cache;
using backend.main.features.events.analytics;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

namespace backend.tests.Unit.Features.Events;

public class EventAnalyticsRepositoryTests
{
    [Fact]
    public async Task GetEventAnalyticsAsync_ShouldReturnCachedDataWithoutHittingDb()
    {
        var expected = new EventAnalyticsData(10, 2, 5, 8, 5000L, 1000L, 200L);
        var refreshCache = new Mock<IRefreshAheadCache>();
        refreshCache
            .Setup(c => c.GetOrSetAsync<EventAnalyticsData>(
                "analytics:event:7",
                It.IsAny<Func<Task<EventAnalyticsData?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()))
            .ReturnsAsync(expected);

        var repo = CreateRepository(refreshCache);

        var result = await repo.GetEventAnalyticsAsync(7);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task GetEventAnalyticsAsync_ShouldUseCorrectCacheKeyAndTtl()
    {
        var refreshCache = new Mock<IRefreshAheadCache>();
        refreshCache
            .Setup(c => c.GetOrSetAsync<EventAnalyticsData>(
                It.IsAny<string>(),
                It.IsAny<Func<Task<EventAnalyticsData?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()))
            .ReturnsAsync(new EventAnalyticsData(0, 0, 0, 0, 0L, 0L, 0L));

        var repo = CreateRepository(refreshCache);

        await repo.GetEventAnalyticsAsync(42);

        refreshCache.Verify(c => c.GetOrSetAsync<EventAnalyticsData>(
            "analytics:event:42",
            It.IsAny<Func<Task<EventAnalyticsData?>>>(),
            TimeSpan.FromMinutes(5),
            It.IsAny<TimeSpan?>(),
            It.IsAny<double>(),
            It.IsAny<System.Text.Json.JsonSerializerOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task GetClubAnalyticsAsync_ShouldReturnCachedDataWithoutHittingDb()
    {
        var expected = new ClubAnalyticsData(
            TotalEvents: 3,
            DraftEvents: 1,
            PublishedEvents: 2,
            CancelledEvents: 0,
            ArchivedEvents: 0,
            UpcomingEvents: 1,
            OngoingEvents: 1,
            PastEvents: 0,
            TotalRegistrations: 20,
            UniqueAttendees: 15,
            RepeatAttendees: 5,
            TotalRevenue: 9900L,
            PendingRevenue: 300L,
            PerEvent: [],
            DailyTrend: [],
            RevenueTrend: []);

        var refreshCache = new Mock<IRefreshAheadCache>();
        refreshCache
            .Setup(c => c.GetOrSetAsync<ClubAnalyticsData>(
                "analytics:club:11",
                It.IsAny<Func<Task<ClubAnalyticsData?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()))
            .ReturnsAsync(expected);

        var repo = CreateRepository(refreshCache);

        var result = await repo.GetClubAnalyticsAsync(11);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task GetClubAnalyticsAsync_ShouldUseCorrectCacheKeyAndTtl()
    {
        var refreshCache = new Mock<IRefreshAheadCache>();
        refreshCache
            .Setup(c => c.GetOrSetAsync<ClubAnalyticsData>(
                It.IsAny<string>(),
                It.IsAny<Func<Task<ClubAnalyticsData?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()))
            .ReturnsAsync(new ClubAnalyticsData(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0L, 0L, [], [], []));

        var repo = CreateRepository(refreshCache);

        await repo.GetClubAnalyticsAsync(99);

        refreshCache.Verify(c => c.GetOrSetAsync<ClubAnalyticsData>(
            "analytics:club:99",
            It.IsAny<Func<Task<ClubAnalyticsData?>>>(),
            TimeSpan.FromMinutes(10),
            It.IsAny<TimeSpan?>(),
            It.IsAny<double>(),
            It.IsAny<System.Text.Json.JsonSerializerOptions?>()),
            Times.Once);
    }

    private static EventAnalyticsRepository CreateRepository(Mock<IRefreshAheadCache> refreshCache)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(connection)
            .Options;

        var context = new AppDatabaseContext(options);
        return new EventAnalyticsRepository(context, refreshCache.Object);
    }
}
