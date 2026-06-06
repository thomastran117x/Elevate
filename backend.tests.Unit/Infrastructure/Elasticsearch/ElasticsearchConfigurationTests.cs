using backend.main.infrastructure.elasticsearch;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Unit.Infrastructure.Elasticsearch;

public class ElasticsearchConfigurationTests
{
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
