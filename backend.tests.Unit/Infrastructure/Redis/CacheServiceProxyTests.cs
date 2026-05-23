using backend.main.features.cache;
using backend.main.infrastructure.redis;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Infrastructure.Redis;

public class CacheServiceProxyTests
{
    [Fact]
    public async Task Proxy_ShouldDelegateToCurrentCacheImplementation_AndReflectStateSwitches()
    {
        var initial = new Mock<ICacheService>();
        initial.Setup(service => service.GetValueAsync("session"))
            .ReturnsAsync("initial");

        var reconnected = new Mock<ICacheService>();
        reconnected.Setup(service => service.GetValueAsync("session"))
            .ReturnsAsync("redis");
        reconnected.Setup(service => service.SetValueAsync("session", "value", null))
            .ReturnsAsync(true);

        var state = new RedisReconnectState(initial.Object);
        var proxy = new CacheServiceProxy(state);

        (await proxy.GetValueAsync("session")).Should().Be("initial");

        state.SwitchToRedis(reconnected.Object);

        (await proxy.GetValueAsync("session")).Should().Be("redis");
        (await proxy.SetValueAsync("session", "value")).Should().BeTrue();

        initial.Verify(service => service.GetValueAsync("session"), Times.Once);
        reconnected.Verify(service => service.GetValueAsync("session"), Times.Once);
        reconnected.Verify(service => service.SetValueAsync("session", "value", null), Times.Once);
    }
}
