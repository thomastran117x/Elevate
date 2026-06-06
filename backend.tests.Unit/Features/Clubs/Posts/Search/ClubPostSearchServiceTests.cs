using System.Reflection;

using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.search;
using backend.main.infrastructure.elasticsearch;

using Elastic.Clients.Elasticsearch;

using FluentAssertions;

namespace backend.tests.Unit.Features.Clubs.Posts.Search;

public class ClubPostSearchServiceTests
{
    [Fact]
    public async Task EnsureIndexAsync_AndSearchMethods_ShouldThrowDisabled_WhenElasticsearchIsNotConfigured()
    {
        var service = CreateService(isConfigured: false);

        var ensureAction = () => service.EnsureIndexAsync();
        var searchByClubAction = () => service.SearchByClubAsync(4, "chess", PostSortBy.Recent, 1, 10);
        var searchAllAction = () => service.SearchAllAsync("chess", PostSortBy.Popular, 1, 10);

        await ensureAction.Should()
            .ThrowAsync<ElasticsearchDisabledException>()
            .WithMessage("*ELASTICSEARCH_URL is not configured*");
        await searchByClubAction.Should()
            .ThrowAsync<ElasticsearchDisabledException>()
            .WithMessage("*ELASTICSEARCH_URL is not configured*");
        await searchAllAction.Should()
            .ThrowAsync<ElasticsearchDisabledException>()
            .WithMessage("*ELASTICSEARCH_URL is not configured*");
    }

    [Fact]
    public async Task EnsureIndexAsync_AndSearchMethods_ShouldThrowConfigurationException_WhenHealthHasFailure()
    {
        var failure = new InvalidOperationException("boot failure");
        var service = CreateService(isConfigured: true, failure: failure);

        var ensureAction = () => service.EnsureIndexAsync();
        var searchByClubAction = () => service.SearchByClubAsync(4, "chess", PostSortBy.Recent, 1, 10);
        var searchAllAction = () => service.SearchAllAsync("chess", PostSortBy.Popular, 1, 10);

        (await ensureAction.Should().ThrowAsync<ElasticsearchConfigurationException>())
            .Which.InnerException.Should().BeSameAs(failure);
        (await searchByClubAction.Should().ThrowAsync<ElasticsearchConfigurationException>())
            .Which.InnerException.Should().BeSameAs(failure);
        (await searchAllAction.Should().ThrowAsync<ElasticsearchConfigurationException>())
            .Which.InnerException.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task WriteOperations_ShouldNoOp_WhenElasticsearchIsNotConfigured()
    {
        var service = CreateService(isConfigured: false);
        var document = new ClubPostDocument { Id = 9, ClubId = 4, UserId = 7, Title = "Notice" };

        var indexAction = () => service.IndexAsync(document);
        var deleteAction = () => service.DeleteAsync(document.Id);
        var bulkAction = () => service.BulkIndexAsync([document]);
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
        var document = new ClubPostDocument { Id = 9, ClubId = 4, UserId = 7, Title = "Notice" };

        var indexAction = () => service.IndexAsync(document);
        var deleteAction = () => service.DeleteAsync(document.Id);
        var bulkAction = () => service.BulkIndexAsync([document]);
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
    public void BuildSort_ShouldExecute_ForRecentAndPopularModes()
    {
        foreach (var sortBy in new[] { PostSortBy.Recent, PostSortBy.Popular })
        {
            var builder = InvokePrivateStatic<Action<SortOptionsDescriptor<ClubPostDocument>>>(
                typeof(ClubPostSearchService),
                "BuildSort",
                sortBy);

            var descriptor = new SortOptionsDescriptor<ClubPostDocument>();
            builder(descriptor);
        }
    }

    [Fact]
    public async Task CanceledTokens_ShouldBeHonored_ByWriteAndEnsureOperations()
    {
        var service = CreateService(isConfigured: false);
        var token = new CancellationToken(canceled: true);

        var ensureAction = () => service.EnsureIndexAsync(token);
        var deleteIndexAction = () => service.DeleteIndexAsync(token);
        var indexAction = () => service.IndexAsync(new ClubPostDocument { Id = 1 }, token);
        var deleteAction = () => service.DeleteAsync(1, token);
        var bulkAction = () => service.BulkIndexAsync([new ClubPostDocument { Id = 1 }], token);

        await ensureAction.Should().ThrowAsync<OperationCanceledException>();
        await deleteIndexAction.Should().ThrowAsync<OperationCanceledException>();
        await indexAction.Should().ThrowAsync<OperationCanceledException>();
        await deleteAction.Should().ThrowAsync<OperationCanceledException>();
        await bulkAction.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ClubPostSearchService CreateService(bool isConfigured, Exception? failure = null)
    {
        var health = new ElasticsearchHealth();
        SetHealth(health, isConfigured, failure);
        return new ClubPostSearchService(new ElasticsearchCircuitBreaker(), health);
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
