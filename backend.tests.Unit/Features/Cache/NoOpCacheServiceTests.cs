using backend.main.features.cache;

using FluentAssertions;

using StackExchange.Redis;

namespace backend.tests.Unit.Features.Cache;

public class NoOpCacheServiceTests
{
    [Fact]
    public async Task Operations_ShouldReturnSafeDefaultValues()
    {
        var service = new NoOpCacheService();

        (await service.GetValueAsync("missing")).Should().BeNull();
        (await service.HashGetAllAsync("hash")).Should().BeEmpty();
        (await service.SetMembersAsync("set")).Should().BeEmpty();
        (await service.GetManyAsync(["a", "b"])).Should().BeEmpty();
        (await service.AcquireLockAsync("lock", "value", TimeSpan.FromMinutes(1))).Should().BeFalse();
        (await service.ReleaseLockAsync("lock", "value")).Should().BeFalse();
        (await service.SetExpiryAsync("key", TimeSpan.FromMinutes(5))).Should().BeFalse();
    }

    [Fact]
    public async Task EvalAsync_ShouldReturnAllowAllResult()
    {
        var service = new NoOpCacheService();

        var result = await service.EvalAsync("return 1", Array.Empty<RedisKey>(), Array.Empty<RedisValue>());

        result.Should().BeOfType<int[]>()
            .Which.Should().Equal(1, 0);
    }

    [Fact]
    public void GetServer_ShouldThrowWhenRedisIsUnavailable()
    {
        var service = new NoOpCacheService();

        var act = () => service.GetServer();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Redis is not available. No server instance.");
    }
}
