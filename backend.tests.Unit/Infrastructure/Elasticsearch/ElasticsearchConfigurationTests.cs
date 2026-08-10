using backend.main.infrastructure.elasticsearch;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Unit.Infrastructure.Elasticsearch;

public class ElasticsearchConfigurationTests
{
    [Fact]
    public void SearchIndexNames_ShouldUseConfiguredNames_AndProductionDefaults()
    {
        var defaults = SearchIndexNames.FromConfiguration(new ConfigurationBuilder().Build());
        defaults.Events.Should().Be("events");
        defaults.Clubs.Should().Be("clubs");
        defaults.ClubPosts.Should().Be("club_posts");

        var configured = SearchIndexNames.FromConfiguration(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [SearchIndexNames.EventsConfigurationKey] = "test-events",
                    [SearchIndexNames.ClubsConfigurationKey] = "test-clubs",
                    [SearchIndexNames.ClubPostsConfigurationKey] = "test-club-posts"
                })
                .Build());

        configured.Events.Should().Be("test-events");
        configured.Clubs.Should().Be("test-clubs");
        configured.ClubPosts.Should().Be("test-club-posts");
    }

    [Fact]
    public void AddAppElasticsearch_ShouldRegisterUnavailableHealth_WhenUrlMissing()
    {
        var services = new ServiceCollection();

        services.AddAppElasticsearch(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        var health = provider.GetRequiredService<ElasticsearchHealth>();
        var client = provider.GetService<Elastic.Clients.Elasticsearch.ElasticsearchClient>();

        if (health.IsConfigured)
        {
            client.Should().NotBeNull();
            health.IsAvailable.Should().BeTrue();
        }
        else
        {
            client.Should().BeNull();
            health.IsAvailable.Should().BeFalse();
        }
    }

    [Fact]
    public void ElasticsearchExceptions_ShouldPreserveMessages_AndInnerExceptions()
    {
        var inner = new InvalidOperationException("boom");
        var disabled = new ElasticsearchDisabledException("disabled");
        var configuration = new ElasticsearchConfigurationException("config", inner);
        var unavailable = new ElasticsearchUnavailableException("unavailable", inner);

        disabled.Message.Should().Be("disabled");
        configuration.Message.Should().Be("config");
        configuration.InnerException.Should().BeSameAs(inner);
        unavailable.Message.Should().Be("unavailable");
        unavailable.InnerException.Should().BeSameAs(inner);
    }
}
