using backend.main.features.cache;

using FluentAssertions;

using Moq;

using StackExchange.Redis;

namespace backend.tests.Unit.Features.Cache;

public class NoOpCacheServiceTests
{
    [Fact]
    public async Task Operations_ShouldReturnSafeDefaultValues()
    {
        var service = new NoOpCacheService();

        (await service.SetValueAsync("key", "value", TimeSpan.FromMinutes(1))).Should().BeFalse();
        (await service.GetValueAsync("missing")).Should().BeNull();
        (await service.IncrementAsync("counter")).Should().Be(0);
        (await service.DecrementAsync("counter")).Should().Be(0);
        (await service.HashSetAsync("hash", "field", "value")).Should().BeFalse();
        (await service.HashGetAsync("hash", "field")).Should().BeNull();
        (await service.HashGetAllAsync("hash")).Should().BeEmpty();
        (await service.HashDeleteAsync("hash", "field")).Should().BeFalse();
        (await service.SetAddAsync("set", "value")).Should().BeFalse();
        (await service.SetRemoveAsync("set", "value")).Should().BeFalse();
        (await service.SetMembersAsync("set")).Should().BeEmpty();
        (await service.ListLeftPushAsync("list", "a")).Should().Be(0);
        (await service.ListRightPushAsync("list", "b")).Should().Be(0);
        (await service.ListLeftPopAsync("list")).Should().BeNull();
        (await service.ListRightPopAsync("list")).Should().BeNull();
        (await service.DeleteKeyAsync("key")).Should().BeFalse();
        (await service.KeyExistsAsync("key")).Should().BeFalse();
        (await service.GetTTLAsync("key")).Should().BeNull();
        (await service.GetManyAsync(["a", "b"])).Should().BeEmpty();
        (await service.AcquireLockAsync("lock", "value", TimeSpan.FromMinutes(1))).Should().BeFalse();
        (await service.ReleaseLockAsync("lock", "value")).Should().BeFalse();
        (await service.SetExpiryAsync("key", TimeSpan.FromMinutes(5))).Should().BeFalse();
        service.ScanKeys(Mock.Of<IServer>(), "prefix:*").Should().BeEmpty();
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
