using backend.main.features.events;
using backend.main.features.events.search;

using FluentAssertions;

using Moq;

using EventEntity = backend.main.features.events.Events;

namespace backend.tests.Unit.Features.Events.Search;

public class EventReindexServiceTests
{
    [Fact]
    public async Task ReindexAllAsync_ShouldDeleteEnsureAndBulkIndexAllEventPages()
    {
        var repository = new Mock<IEventsRepository>();
        var searchService = new Mock<IEventSearchService>();
        var batches = new List<List<EventDocument>>();

        var firstPage = Enumerable.Range(1, 100)
            .Select(CreateEvent)
            .ToList();
        var secondPage = new List<EventEntity>
        {
            CreateEvent(101),
            CreateEvent(102)
        };

        repository.Setup(repo => repo.GetAllForReindexAsync(1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstPage);
        repository.Setup(repo => repo.GetAllForReindexAsync(2, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondPage);

        searchService.Setup(service => service.BulkIndexAsync(It.IsAny<IEnumerable<EventDocument>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<EventDocument>, CancellationToken>((documents, _) => batches.Add(documents.ToList()))
            .Returns(Task.CompletedTask);

        var service = new EventReindexService(repository.Object, searchService.Object);

        var result = await service.ReindexAllAsync();

        result.Should().Be(102);
        batches.Should().HaveCount(2);
        batches[0][0].Name.Should().Be("Event 1");
        batches[1].Select(document => document.Id).Should().Equal(101, 102);
        searchService.Verify(service => service.DeleteIndexAsync(It.IsAny<CancellationToken>()), Times.Once);
        searchService.Verify(service => service.EnsureIndexAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static EventEntity CreateEvent(int id) => new()
    {
        Id = id,
        ClubId = 7,
        Name = $"Event {id}",
        Description = $"Description {id}",
        Location = "Student Center",
        Category = EventCategory.Fitness,
        StartTime = new DateTime(2026, 5, 10, 18, 0, 0, DateTimeKind.Utc),
        EndTime = new DateTime(2026, 5, 10, 20, 0, 0, DateTimeKind.Utc),
        LifecycleState = EventLifecycleState.Published,
        CreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
        Tags = ["fitness", "community"],
        RegistrationCount = id
    };
}
