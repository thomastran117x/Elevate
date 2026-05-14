using backend.main.features.clubs;
using backend.main.features.clubs.search;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace backend.tests.Clubs;

public class ClubRepositorySearchTests
{
    [Fact]
    public async Task SearchAsync_ShouldExcludePrivateClubs_AndSortByMembers()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AppDatabaseContext(options);
        await context.Database.EnsureCreatedAsync();

        context.Clubs.AddRange(
            new Club
            {
                UserId = 1,
                Name = "Chess Club",
                Description = "Strategy nights",
                Clubtype = ClubType.Academic,
                ClubImage = "chess.png",
                MemberCount = 10,
                isPrivate = false,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Club
            {
                UserId = 2,
                Name = "Hidden Chess Club",
                Description = "Private strategy nights",
                Clubtype = ClubType.Academic,
                ClubImage = "hidden.png",
                MemberCount = 99,
                isPrivate = true,
                CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            },
            new Club
            {
                UserId = 3,
                Name = "Robotics Club",
                Description = "Build things",
                Clubtype = ClubType.Academic,
                ClubImage = "robotics.png",
                MemberCount = 25,
                isPrivate = false,
                CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)
            });

        await context.SaveChangesAsync();

        var repository = new ClubRepository(context);

        var (items, totalCount) = await repository.SearchAsync(new ClubSearchCriteria
        {
            Query = "Club",
            ClubType = ClubType.Academic,
            SortBy = ClubSortBy.Members,
            Page = 1,
            PageSize = 10
        });

        totalCount.Should().Be(2);
        items.Select(item => item.Name).Should().Equal("Robotics Club", "Chess Club");
        items.Should().OnlyContain(item => !item.isPrivate);
    }
}
