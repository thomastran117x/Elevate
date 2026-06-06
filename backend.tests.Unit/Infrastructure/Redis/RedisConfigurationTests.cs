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
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

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
        hostedServices.Should().ContainSingle(service => service is RedisReconnectBackgroundService);
    }
}
