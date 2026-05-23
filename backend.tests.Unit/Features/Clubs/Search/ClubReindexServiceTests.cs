using backend.main.features.clubs;
using backend.main.features.clubs.search;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Clubs.Search;

public class ClubReindexServiceTests
{
    [Fact]
    public async Task ReindexAllAsync_ShouldDeleteEnsureAndBulkIndexAllRepositoryPages()
    {
        var repository = new Mock<IClubRepository>();
        var searchService = new Mock<IClubSearchService>();
        var batches = new List<List<ClubDocument>>();

        var firstPage = Enumerable.Range(1, 100)
            .Select(CreateClub)
            .ToList();
        var secondPage = new List<Club>
        {
            CreateClub(101),
            CreateClub(102)
        };

        repository.Setup(repo => repo.GetAllForReindexAsync(1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstPage);
        repository.Setup(repo => repo.GetAllForReindexAsync(2, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondPage);

        searchService.Setup(service => service.BulkIndexAsync(It.IsAny<IEnumerable<ClubDocument>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ClubDocument>, CancellationToken>((documents, _) => batches.Add(documents.ToList()))
            .Returns(Task.CompletedTask);

        var service = new ClubReindexService(repository.Object, searchService.Object);

        var result = await service.ReindexAllAsync();

        result.Should().Be(102);
        batches.Should().HaveCount(2);
        batches[0].Should().HaveCount(100);
        batches[1].Select(document => document.Id).Should().Equal(101, 102);
        searchService.Verify(service => service.DeleteIndexAsync(It.IsAny<CancellationToken>()), Times.Once);
        searchService.Verify(service => service.EnsureIndexAsync(It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(repo => repo.GetAllForReindexAsync(3, 100, It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Club CreateClub(int id) => new()
    {
        Id = id,
        UserId = 7,
        Name = $"Club {id}",
        Description = $"Description {id}",
        Clubtype = ClubType.Social,
        ClubImage = $"https://cdn.test/clubs/{id}.png",
        Location = "Campus",
        CreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)
    };
}
