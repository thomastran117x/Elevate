using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.search;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Clubs.Search;

public class ClubPostReindexServiceTests
{
    [Fact]
    public async Task ReindexAllAsync_ShouldProjectPostsIntoSearchDocumentsAcrossAllPages()
    {
        var repository = new Mock<IClubPostRepository>();
        var searchService = new Mock<IClubPostSearchService>();
        var batches = new List<List<ClubPostDocument>>();

        var firstPage = Enumerable.Range(1, 100)
            .Select(CreatePost)
            .ToList();
        var secondPage = new List<ClubPost>
        {
            CreatePost(101),
            CreatePost(102)
        };

        repository.Setup(repo => repo.GetAllForReindexAsync(1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstPage);
        repository.Setup(repo => repo.GetAllForReindexAsync(2, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondPage);

        searchService.Setup(service => service.BulkIndexAsync(It.IsAny<IEnumerable<ClubPostDocument>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ClubPostDocument>, CancellationToken>((documents, _) => batches.Add(documents.ToList()))
            .Returns(Task.CompletedTask);

        var service = new ClubPostReindexService(repository.Object, searchService.Object);

        var result = await service.ReindexAllAsync();

        result.Should().Be(102);
        batches.Should().HaveCount(2);
        batches[0][0].Title.Should().Be("Post 1");
        batches[1].Select(document => document.Id).Should().Equal(101, 102);
        searchService.Verify(service => service.DeleteIndexAsync(It.IsAny<CancellationToken>()), Times.Once);
        searchService.Verify(service => service.EnsureIndexAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ClubPost CreatePost(int id) => new()
    {
        Id = id,
        ClubId = 7,
        UserId = 9,
        Title = $"Post {id}",
        Content = $"Content {id}",
        PostType = PostType.Announcement,
        LikesCount = id,
        CreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)
    };
}
