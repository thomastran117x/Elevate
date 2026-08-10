using backend.main.features.cache;
using backend.main.infrastructure.redis;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace backend.tests.Unit.Infrastructure.Redis;

public class RedisConfigurationTests
{
    [Fact]
    public void AddAppRedis_ShouldRegisterFallbackServices_WhenRedisConnectionFails()
    {
        const string configuredConnectionString =
            "127.0.0.1:1,abortConnect=true,connectTimeout=50,connectRetry=0";
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:ConnectionString"] = configuredConnectionString
            })
            .Build();

        services.AddAppRedis(configuration);

        using var provider = services.BuildServiceProvider();

        var health = provider.GetRequiredService<RedisHealth>();
        var state = provider.GetRequiredService<RedisReconnectState>();
        var cache = provider.GetRequiredService<ICacheService>();
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        health.IsAvailable.Should().BeFalse();
        health.Failure.Should().NotBeNull();
        state.Current.Should().BeOfType<NoOpCacheService>();
        cache.Should().BeOfType<CacheServiceProxy>();
        var reconnectService = hostedServices.Should()
            .ContainSingle(service => service is RedisReconnectBackgroundService)
            .Which.Should()
            .BeOfType<RedisReconnectBackgroundService>()
            .Subject;
        reconnectService.ConnectionString.Should().Be(configuredConnectionString);
    }
}
