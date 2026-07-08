using backend.main.features.clubs;
using backend.main.features.clubs.search;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

namespace backend.tests.Clubs;

public class ClubRepositorySearchTests
{
    [Fact]
    public async Task SearchAsync_ShouldExcludePrivateClubs_AndSortByMembers()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();

        context.Users.AddRange(
            new User { Id = 1, Email = "owner1@test.local", Usertype = "Organizer" },
            new User { Id = 2, Email = "owner2@test.local", Usertype = "Organizer" },
            new User { Id = 3, Email = "owner3@test.local", Usertype = "Organizer" });

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
