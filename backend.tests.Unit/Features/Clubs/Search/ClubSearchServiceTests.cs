using System.Reflection;

using backend.main.features.clubs;
using backend.main.features.clubs.search;
using backend.main.infrastructure.elasticsearch;

using Elastic.Clients.Elasticsearch.QueryDsl;

using FluentAssertions;

namespace backend.tests.Unit.Features.Clubs.Search;

public class ClubSearchServiceTests
{
    [Fact]
    public async Task EnsureIndexAsync_AndSearchAsync_ShouldThrowDisabled_WhenElasticsearchIsNotConfigured()
    {
        var service = CreateService(isConfigured: false);

        var ensureAction = () => service.EnsureIndexAsync();
        var searchAction = () => service.SearchAsync(new ClubSearchCriteria());

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
        var searchAction = () => service.SearchAsync(new ClubSearchCriteria());

        var ensureException = await ensureAction.Should().ThrowAsync<ElasticsearchConfigurationException>();
        ensureException.Which.InnerException.Should().BeSameAs(failure);

        var searchException = await searchAction.Should().ThrowAsync<ElasticsearchConfigurationException>();
        searchException.Which.InnerException.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task WriteOperations_ShouldNoOp_WhenElasticsearchIsNotConfigured()
    {
        var service = CreateService(isConfigured: false);

        var indexAction = () => service.IndexAsync(new ClubDocument { Id = 1, Name = "Club" });
        var deleteAction = () => service.DeleteAsync(1);
        var bulkAction = () => service.BulkIndexAsync(
            [new ClubDocument { Id = 1, Name = "Club" }]);
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

        var indexAction = () => service.IndexAsync(new ClubDocument { Id = 1, Name = "Club" });
        var deleteAction = () => service.DeleteAsync(1);
        var bulkAction = () => service.BulkIndexAsync(
            [new ClubDocument { Id = 1, Name = "Club" }]);
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
    public void BuildFilters_ShouldAlwaysIncludePublicFilter_AndOptionalClubType()
    {
        var defaultFilters = InvokePrivateStatic<Action<QueryDescriptor<ClubDocument>>[]>(
            typeof(ClubSearchService),
            "BuildFilters",
            new ClubSearchCriteria());
        var typeFilters = InvokePrivateStatic<Action<QueryDescriptor<ClubDocument>>[]>(
            typeof(ClubSearchService),
            "BuildFilters",
            new ClubSearchCriteria
            {
                ClubType = ClubType.Gaming
            });

        defaultFilters.Should().HaveCount(1);
        typeFilters.Should().HaveCount(2);

        foreach (var filter in typeFilters)
        {
            var descriptor = new QueryDescriptor<ClubDocument>();
            filter(descriptor);
        }
    }

    [Fact]
    public void HelperBuilders_ShouldExecute_ForTextQueryAndAllSortModes()
    {
        var relevanceQuery = new QueryDescriptor<ClubDocument>();
        var membersQuery = new QueryDescriptor<ClubDocument>();

        InvokePrivateStatic<object?>(
            typeof(ClubSearchService),
            "BuildTextQuery",
            relevanceQuery,
            "board games",
            ClubSortBy.Relevance);
        InvokePrivateStatic<object?>(
            typeof(ClubSearchService),
            "BuildTextQuery",
            membersQuery,
            "board games",
            ClubSortBy.Members);

        foreach (var sortBy in new[] { ClubSortBy.Relevance, ClubSortBy.Newest, ClubSortBy.Members, ClubSortBy.Rating })
        {
            var descriptor = new Elastic.Clients.Elasticsearch.SearchRequestDescriptor<ClubDocument>();
            InvokePrivateStatic<object?>(
                typeof(ClubSearchService),
                "ApplySort",
                descriptor,
                new ClubSearchCriteria { SortBy = sortBy });
        }
    }

    private static ClubSearchService CreateService(bool isConfigured, Exception? failure = null)
    {
        var health = new ElasticsearchHealth();
        SetHealth(health, isConfigured, failure);
        return new ClubSearchService(new ElasticsearchCircuitBreaker(), health);
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
