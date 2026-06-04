using System.Reflection;

using backend.main.features.events;
using backend.main.features.events.search;
using backend.main.infrastructure.elasticsearch;

using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;

using FluentAssertions;

namespace backend.tests.Unit.Features.Events.Search;

public class EventSearchServiceTests
{
    [Fact]
    public async Task EnsureIndexAsync_AndSearchAsync_ShouldThrowDisabled_WhenElasticsearchIsNotConfigured()
    {
        var service = CreateService(isConfigured: false);

        var ensureAction = () => service.EnsureIndexAsync();
        var searchAction = () => service.SearchAsync(new EventSearchCriteria());

        await ensureAction.Should()
            .ThrowAsync<ElasticsearchDisabledException>()
            .WithMessage("*ELASTICSEARCH_URL is not configured*");
        await searchAction.Should()
            .ThrowAsync<ElasticsearchDisabledException>()
            .WithMessage("*ELASTICSEARCH_URL is not configured*");
    }

    [Fact]
    public async Task EnsureIndexAsync_AndSearchAsync_ShouldThrowConfigurationException_WhenHealthHasFailure()
    {
        var failure = new InvalidOperationException("boot failure");
        var service = CreateService(isConfigured: true, failure: failure);

        var ensureAction = () => service.EnsureIndexAsync();
        var searchAction = () => service.SearchAsync(new EventSearchCriteria());

        var ensureException = await ensureAction.Should().ThrowAsync<ElasticsearchConfigurationException>();
        ensureException.Which.InnerException.Should().BeSameAs(failure);

        var searchException = await searchAction.Should().ThrowAsync<ElasticsearchConfigurationException>();
        searchException.Which.InnerException.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task WriteOperations_ShouldNoOp_WhenElasticsearchIsNotConfigured()
    {
        var service = CreateService(isConfigured: false);

        var indexAction = () => service.IndexAsync(new EventDocument { Id = 1, Name = "Event" });
        var deleteAction = () => service.DeleteAsync(1);
        var bulkAction = () => service.BulkIndexAsync(
            [new EventDocument { Id = 1, Name = "Event" }]);
        var deleteIndexAction = () => service.DeleteIndexAsync();

        await indexAction.Should().NotThrowAsync();
        await deleteAction.Should().NotThrowAsync();
        await bulkAction.Should().NotThrowAsync();
        await deleteIndexAction.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WriteOperations_ShouldThrowConfigurationException_WhenHealthHasFailure()
    {
        var failure = new InvalidOperationException("boot failure");
        var service = CreateService(isConfigured: true, failure: failure);

        var indexAction = () => service.IndexAsync(new EventDocument { Id = 1, Name = "Event" });
        var deleteAction = () => service.DeleteAsync(1);
        var bulkAction = () => service.BulkIndexAsync(
            [new EventDocument { Id = 1, Name = "Event" }]);
        var deleteIndexAction = () => service.DeleteIndexAsync();

        (await indexAction.Should().ThrowAsync<ElasticsearchConfigurationException>())
            .Which.InnerException.Should().BeSameAs(failure);
        (await deleteAction.Should().ThrowAsync<ElasticsearchConfigurationException>())
            .Which.InnerException.Should().BeSameAs(failure);
        (await bulkAction.Should().ThrowAsync<ElasticsearchConfigurationException>())
            .Which.InnerException.Should().BeSameAs(failure);
        (await deleteIndexAction.Should().ThrowAsync<ElasticsearchConfigurationException>())
            .Which.InnerException.Should().BeSameAs(failure);
    }

    [Fact]
    public void BuildFilters_ShouldCreateExpectedPredicates_ForCombinedCriteria()
    {
        var criteria = new EventSearchCriteria
        {
            IsPrivate = false,
            LifecycleState = EventLifecycleState.Published,
            ClubId = 7,
            Category = EventCategory.Gaming,
            Tags = ["indoor", "team-play"],
            City = " Toronto ",
            Lat = 43.65,
            Lng = -79.38,
            RadiusKm = 15,
            Status = EventStatus.Ongoing
        };

        var filters = InvokePrivateStatic<Action<QueryDescriptor<EventDocument>>[]>(
            typeof(EventSearchService),
            "BuildFilters",
            criteria,
            new DateTime(2026, 6, 4, 12, 0, 0, DateTimeKind.Utc),
            true);

        filters.Should().HaveCount(9);

        foreach (var filter in filters)
        {
            var descriptor = new QueryDescriptor<EventDocument>();
            filter(descriptor);
        }
    }

    [Fact]
    public void BuildFilters_ShouldSupportUpcomingAndClosedStatuses()
    {
        var upcoming = new EventSearchCriteria { Status = EventStatus.Upcoming };
        var closed = new EventSearchCriteria { Status = EventStatus.Closed };

        var upcomingFilters = InvokePrivateStatic<Action<QueryDescriptor<EventDocument>>[]>(
            typeof(EventSearchService),
            "BuildFilters",
            upcoming,
            DateTime.UtcNow,
            false);
        var closedFilters = InvokePrivateStatic<Action<QueryDescriptor<EventDocument>>[]>(
            typeof(EventSearchService),
            "BuildFilters",
            closed,
            DateTime.UtcNow,
            false);

        upcomingFilters.Should().HaveCount(1);
        closedFilters.Should().HaveCount(1);
        upcomingFilters[0](new QueryDescriptor<EventDocument>());
        closedFilters[0](new QueryDescriptor<EventDocument>());
    }

    [Fact]
    public void HelperBuilders_ShouldExecute_ForTextSortAndDistanceBranches()
    {
        var relevanceQuery = new QueryDescriptor<EventDocument>();
        var dateQuery = new QueryDescriptor<EventDocument>();

        InvokePrivateStatic<object?>(
            typeof(EventSearchService),
            "BuildTextQuery",
            relevanceQuery,
            "board games",
            EventSortBy.Relevance);
        InvokePrivateStatic<object?>(
            typeof(EventSearchService),
            "BuildTextQuery",
            dateQuery,
            "board games",
            EventSortBy.Date);

        var distanceSort = new Elastic.Clients.Elasticsearch.SearchRequestDescriptor<EventDocument>();
        var dateSort = new Elastic.Clients.Elasticsearch.SearchRequestDescriptor<EventDocument>();

        InvokePrivateStatic<object?>(
            typeof(EventSearchService),
            "ApplySort",
            distanceSort,
            new EventSearchCriteria
            {
                SortBy = EventSortBy.Distance,
                Lat = 43.65,
                Lng = -79.38
            },
            true);
        InvokePrivateStatic<object?>(
            typeof(EventSearchService),
            "ApplySort",
            dateSort,
            new EventSearchCriteria
            {
                SortBy = EventSortBy.Date
            },
            false);
    }

    [Fact]
    public void ExtractDistanceKm_ShouldReturnRoundedKilometers_WhenDistanceSortIsPresent()
    {
        var hit = new Hit<EventDocument>
        {
            Sort = [(Elastic.Clients.Elasticsearch.FieldValue)1234.5]
        };

        var distance = InvokePrivateStatic<double?>(
            typeof(EventSearchService),
            "ExtractDistanceKm",
            hit,
            true);
        var noDistance = InvokePrivateStatic<double?>(
            typeof(EventSearchService),
            "ExtractDistanceKm",
            hit,
            false);

        distance.Should().Be(1.234);
        noDistance.Should().BeNull();
    }

    private static EventSearchService CreateService(bool isConfigured, Exception? failure = null)
    {
        var health = new ElasticsearchHealth();
        SetHealth(health, isConfigured, failure);
        return new EventSearchService(new ElasticsearchCircuitBreaker(), health);
    }

    private static void SetHealth(ElasticsearchHealth health, bool isConfigured, Exception? failure)
    {
        typeof(ElasticsearchHealth).GetProperty(nameof(ElasticsearchHealth.IsConfigured))!
            .SetValue(health, isConfigured);
        typeof(ElasticsearchHealth).GetProperty(nameof(ElasticsearchHealth.Failure))!
            .SetValue(health, failure);
    }

    private static T InvokePrivateStatic<T>(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        return (T)method!.Invoke(null, args)!;
    }
}
