using backend.main.features.cache;
using backend.main.infrastructure.redis;

using FluentAssertions;

using Moq;

using StackExchange.Redis;

namespace backend.tests.Unit.Infrastructure.Redis;

public class CacheServiceProxyTests
{
    [Fact]
    public async Task CacheServiceProxy_ShouldDelegateAllOperations_ToCurrentCacheService()
    {
        var current = new Mock<ICacheService>();
        current.Setup(service => service.SetValueAsync("k", "v", It.IsAny<TimeSpan?>())).ReturnsAsync(true);
        current.Setup(service => service.GetValueAsync("k")).ReturnsAsync("v");
        current.Setup(service => service.IncrementAsync("count", 2)).ReturnsAsync(3);
        current.Setup(service => service.DecrementAsync("count", 1)).ReturnsAsync(2);
        current.Setup(service => service.HashSetAsync("hash", "field", "value")).ReturnsAsync(true);
        current.Setup(service => service.HashGetAsync("hash", "field")).ReturnsAsync("value");
        current.Setup(service => service.HashGetAllAsync("hash")).ReturnsAsync(new Dictionary<string, string> { ["field"] = "value" });
        current.Setup(service => service.HashDeleteAsync("hash", "field")).ReturnsAsync(true);
        current.Setup(service => service.SetAddAsync("set", "a")).ReturnsAsync(true);
        current.Setup(service => service.SetRemoveAsync("set", "a")).ReturnsAsync(true);
        current.Setup(service => service.SetMembersAsync("set")).ReturnsAsync(["a", "b"]);
        current.Setup(service => service.ListLeftPushAsync("list", "a")).ReturnsAsync(1);
        current.Setup(service => service.ListRightPushAsync("list", "b")).ReturnsAsync(2);
        current.Setup(service => service.ListLeftPopAsync("list")).ReturnsAsync("a");
        current.Setup(service => service.ListRightPopAsync("list")).ReturnsAsync("b");
        current.Setup(service => service.DeleteKeyAsync("gone")).ReturnsAsync(true);
        current.Setup(service => service.KeyExistsAsync("k")).ReturnsAsync(true);
        current.Setup(service => service.GetTTLAsync("k")).ReturnsAsync(TimeSpan.FromMinutes(1));
        current.Setup(service => service.SetExpiryAsync("k", It.IsAny<TimeSpan>())).ReturnsAsync(true);
        current.Setup(service => service.AcquireLockAsync("lock", "owner", It.IsAny<TimeSpan>())).ReturnsAsync(true);
        current.Setup(service => service.ReleaseLockAsync("lock", "owner")).ReturnsAsync(true);
        var server = new Mock<IServer>().Object;
        current.Setup(service => service.GetServer()).Returns(server);
        current.Setup(service => service.ScanKeys(server, "clubs:*")).Returns(["clubs:1"]);
        current.Setup(service => service.GetManyAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string?> { ["a"] = "1" });
        current.Setup(service => service.EvalAsync("return 1", It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>()))
            .ReturnsAsync(new[] { 1, 0 });

        var state = new RedisReconnectState(current.Object);
        var proxy = new CacheServiceProxy(state);

        (await proxy.SetValueAsync("k", "v")).Should().BeTrue();
        (await proxy.GetValueAsync("k")).Should().Be("v");
        (await proxy.IncrementAsync("count", 2)).Should().Be(3);
        (await proxy.DecrementAsync("count", 1)).Should().Be(2);
        (await proxy.HashSetAsync("hash", "field", "value")).Should().BeTrue();
        (await proxy.HashGetAsync("hash", "field")).Should().Be("value");
        (await proxy.HashGetAllAsync("hash"))["field"].Should().Be("value");
        (await proxy.HashDeleteAsync("hash", "field")).Should().BeTrue();
        (await proxy.SetAddAsync("set", "a")).Should().BeTrue();
        (await proxy.SetRemoveAsync("set", "a")).Should().BeTrue();
        (await proxy.SetMembersAsync("set")).Should().Equal("a", "b");
        (await proxy.ListLeftPushAsync("list", "a")).Should().Be(1);
        (await proxy.ListRightPushAsync("list", "b")).Should().Be(2);
        (await proxy.ListLeftPopAsync("list")).Should().Be("a");
        (await proxy.ListRightPopAsync("list")).Should().Be("b");
        (await proxy.DeleteKeyAsync("gone")).Should().BeTrue();
        (await proxy.KeyExistsAsync("k")).Should().BeTrue();
        (await proxy.GetTTLAsync("k")).Should().Be(TimeSpan.FromMinutes(1));
        (await proxy.SetExpiryAsync("k", TimeSpan.FromMinutes(2))).Should().BeTrue();
        (await proxy.AcquireLockAsync("lock", "owner", TimeSpan.FromSeconds(5))).Should().BeTrue();
        (await proxy.ReleaseLockAsync("lock", "owner")).Should().BeTrue();
        proxy.GetServer().Should().BeSameAs(server);
        proxy.ScanKeys(server, "clubs:*").Should().Equal("clubs:1");
        (await proxy.GetManyAsync(["a"]))["a"].Should().Be("1");
        await proxy.EvalAsync("return 1", ["k"], ["v"]);

        current.VerifyAll();
    }

    [Fact]
    public async Task CacheServiceProxy_ShouldUseSwitchedRedisCache_AfterReconnect()
    {
        var initial = new Mock<ICacheService>();
        initial.Setup(service => service.GetValueAsync("k")).ReturnsAsync("initial");

        var reconnected = new Mock<ICacheService>();
        reconnected.Setup(service => service.GetValueAsync("k")).ReturnsAsync("redis");

        var state = new RedisReconnectState(initial.Object);
        var proxy = new CacheServiceProxy(state);

        (await proxy.GetValueAsync("k")).Should().Be("initial");

        state.SwitchToRedis(reconnected.Object);

        (await proxy.GetValueAsync("k")).Should().Be("redis");
    }
}
