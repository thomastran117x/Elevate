using System.Text.Json;
using System.Reflection;

using backend.main.features.cache;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Cache;

public class RefreshAheadCacheTests
{
    // Simple types used as cached values
    private sealed record Item(int Id);
    private sealed record ItemDto(int Id);

    private static Item FromDto(ItemDto d) => new(d.Id);
    private static ItemDto ToDto(Item i) => new(i.Id);

    // ──────────────────────────────────────────────────────────
    // GetOrSetAsync<T> — single-type variant
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_CallsFactoryAndStoresValue()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync("key")).ReturnsAsync((string?)null);
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);

        var sut = new RefreshAheadCache(cache.Object);
        var item = new Item(42);

        var result = await sut.GetOrSetAsync("key", () => Task.FromResult<Item?>(item), TimeSpan.FromMinutes(5));

        result.Should().Be(item);
        cache.Verify(c => c.SetValueAsync("key", It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheHit_DeserializesAndDoesNotCallFactory()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync("key"))
            .ReturnsAsync(JsonSerializer.Serialize(new Item(7)));

        var sut = new RefreshAheadCache(cache.Object);
        var factoryCalled = false;

        var result = await sut.GetOrSetAsync<Item>("key", () =>
        {
            factoryCalled = true;
            return Task.FromResult<Item?>(null);
        }, TimeSpan.FromMinutes(5));

        result!.Id.Should().Be(7);
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrSetAsync_NullSentinel_ReturnsNullWithoutCallingFactory()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync("key")).ReturnsAsync("__null__");

        var sut = new RefreshAheadCache(cache.Object);
        var factoryCalled = false;

        var result = await sut.GetOrSetAsync<Item>("key", () =>
        {
            factoryCalled = true;
            return Task.FromResult<Item?>(null);
        }, TimeSpan.FromMinutes(5));

        result.Should().BeNull();
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryReturnsNull_StoresSentinelAndReturnsNull()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync("key")).ReturnsAsync((string?)null);
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);

        var sut = new RefreshAheadCache(cache.Object);
        var sentinelTtl = TimeSpan.FromSeconds(15);

        var result = await sut.GetOrSetAsync<Item>("key", () => Task.FromResult<Item?>(null), TimeSpan.FromMinutes(5), sentinelTtl);

        result.Should().BeNull();
        cache.Verify(c => c.SetValueAsync("key", "__null__", sentinelTtl), Times.Once);
    }

    [Fact]
    public async Task GetOrSetAsync_WithSerializerOptions_PassesThroughToJsonSerializer()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var cache = new Mock<ICacheService>();
        string? storedJson = null;
        cache.Setup(c => c.GetValueAsync("key")).ReturnsAsync((string?)null);
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Callback<string, string, TimeSpan?>((_, v, _) => storedJson = v)
            .ReturnsAsync(true);

        var sut = new RefreshAheadCache(cache.Object);

        await sut.GetOrSetAsync("key", () => Task.FromResult<Item?>(new Item(1)), TimeSpan.FromMinutes(5), serializerOptions: options);

        storedJson.Should().Contain("\"id\""); // camelCase via options
    }

    [Fact]
    public async Task GetOrSetAsync_StaleCacheHit_TriggersBackgroundRefresh()
    {
        var ttl = TimeSpan.FromMilliseconds(50);
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);

        var sut = new RefreshAheadCache(cache.Object);

        // Populate expiry tracker via SetAsync
        await sut.SetAsync("key", new Item(1), ttl);

        await WaitUntilWithinRefreshWindowAsync(sut, "key", ttl);

        var refreshSignal = new TaskCompletionSource<bool>();
        cache.Setup(c => c.GetValueAsync("key"))
            .ReturnsAsync(JsonSerializer.Serialize(new Item(1)));

        await sut.GetOrSetAsync(
            "key",
            () =>
            {
                refreshSignal.TrySetResult(true);
                return Task.FromResult<Item?>(new Item(99));
            },
            ttl);

        var refreshed = await refreshSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        refreshed.Should().BeTrue("background refresh-ahead should have fired");
    }

    [Fact]
    public async Task GetOrSetAsync_StaleCacheHit_WhenRefreshReturnsNull_StoresSentinel()
    {
        var ttl = TimeSpan.FromMilliseconds(50);
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);

        var sut = new RefreshAheadCache(cache.Object);
        await sut.SetAsync("key", new Item(1), ttl);
        await WaitUntilWithinRefreshWindowAsync(sut, "key", ttl);

        cache.Setup(c => c.GetValueAsync("key"))
            .ReturnsAsync(JsonSerializer.Serialize(new Item(1)));

        var refreshSignal = new TaskCompletionSource<bool>();
        await sut.GetOrSetAsync(
            "key",
            () =>
            {
                refreshSignal.TrySetResult(true);
                return Task.FromResult<Item?>(null);
            },
            ttl);

        await refreshSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        cache.Verify(c => c.SetValueAsync("key", "__null__", It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetOrSetAsync_StaleCacheHit_DeduplicatesConcurrentRefreshes()
    {
        var ttl = TimeSpan.FromMilliseconds(50);
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.GetValueAsync("key"))
            .ReturnsAsync(JsonSerializer.Serialize(new Item(1)));

        var sut = new RefreshAheadCache(cache.Object);
        await sut.SetAsync("key", new Item(1), ttl);
        await WaitUntilWithinRefreshWindowAsync(sut, "key", ttl);

        var calls = 0;
        var release = new TaskCompletionSource<bool>();

        var first = sut.GetOrSetAsync(
            "key",
            async () =>
            {
                Interlocked.Increment(ref calls);
                await release.Task.WaitAsync(TimeSpan.FromSeconds(2));
                return new Item(2);
            },
            ttl);

        var second = sut.GetOrSetAsync(
            "key",
            async () =>
            {
                Interlocked.Increment(ref calls);
                await release.Task.WaitAsync(TimeSpan.FromSeconds(2));
                return new Item(3);
            },
            ttl);

        await Task.WhenAll(first, second);
        release.TrySetResult(true);
        await Task.Delay(100);

        calls.Should().Be(1, "only one background refresh should run per key at a time");
    }

    [Fact]
    public async Task GetOrSetAsync_CacheHit_WithSerializerOptions_UsesDeserializeOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync("key"))
            .ReturnsAsync("{\"id\":11}");

        var sut = new RefreshAheadCache(cache.Object);

        var result = await sut.GetOrSetAsync<Item>(
            "key",
            () => Task.FromResult<Item?>(null),
            TimeSpan.FromMinutes(5),
            serializerOptions: options);

        result!.Id.Should().Be(11);
    }

    // ──────────────────────────────────────────────────────────
    // GetOrSetAsync<TEntity, TStored> — mapped variant
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrSetAsync_MappedVariant_CacheMiss_StoresToStoredTypeAndReturnsEntity()
    {
        var cache = new Mock<ICacheService>();
        string? stored = null;
        cache.Setup(c => c.GetValueAsync("key")).ReturnsAsync((string?)null);
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Callback<string, string, TimeSpan?>((_, v, _) => stored = v)
            .ReturnsAsync(true);

        var sut = new RefreshAheadCache(cache.Object);
        var item = new Item(5);

        var result = await sut.GetOrSetAsync("key", () => Task.FromResult<Item?>(item), ToDto, FromDto, TimeSpan.FromMinutes(5));

        result.Should().Be(item);
        stored.Should().NotBeNull();
        var dto = JsonSerializer.Deserialize<ItemDto>(stored!);
        dto!.Id.Should().Be(5);
    }

    [Fact]
    public async Task GetOrSetAsync_MappedVariant_CacheHit_DeserializesAsStoredAndMapsToEntity()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync("key"))
            .ReturnsAsync(JsonSerializer.Serialize(new ItemDto(9)));

        var sut = new RefreshAheadCache(cache.Object);
        var factoryCalled = false;

        var result = await sut.GetOrSetAsync<Item, ItemDto>("key", () =>
        {
            factoryCalled = true;
            return Task.FromResult<Item?>(null);
        }, ToDto, FromDto, TimeSpan.FromMinutes(5));

        result!.Id.Should().Be(9);
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrSetAsync_MappedVariant_NullSentinel_ReturnsNull()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync("key")).ReturnsAsync("__null__");

        var sut = new RefreshAheadCache(cache.Object);

        var result = await sut.GetOrSetAsync("key", () => Task.FromResult<Item?>(null), ToDto, FromDto, TimeSpan.FromMinutes(5));

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrSetAsync_MappedVariant_StaleCacheHit_TriggersBackgroundRefresh()
    {
        var ttl = TimeSpan.FromMilliseconds(50);
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);

        var sut = new RefreshAheadCache(cache.Object);
        await sut.SetAsync("key", new ItemDto(1), ttl);
        await WaitUntilWithinRefreshWindowAsync(sut, "key", ttl);

        cache.Setup(c => c.GetValueAsync("key"))
            .ReturnsAsync(JsonSerializer.Serialize(new ItemDto(1)));

        var refreshSignal = new TaskCompletionSource<bool>();
        await sut.GetOrSetAsync(
            "key",
            () =>
            {
                refreshSignal.TrySetResult(true);
                return Task.FromResult<Item?>(new Item(8));
            },
            ToDto,
            FromDto,
            ttl);

        var refreshed = await refreshSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        refreshed.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrSetAsync_MappedVariant_StaleCacheHit_WhenRefreshReturnsNull_StoresSentinel()
    {
        var ttl = TimeSpan.FromMilliseconds(50);
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);

        var sut = new RefreshAheadCache(cache.Object);
        await sut.SetAsync("key", new ItemDto(1), ttl);
        await WaitUntilWithinRefreshWindowAsync(sut, "key", ttl);

        cache.Setup(c => c.GetValueAsync("key"))
            .ReturnsAsync(JsonSerializer.Serialize(new ItemDto(1)));

        var refreshSignal = new TaskCompletionSource<bool>();
        await sut.GetOrSetAsync(
            "key",
            () =>
            {
                refreshSignal.TrySetResult(true);
                return Task.FromResult<Item?>(null);
            },
            ToDto,
            FromDto,
            ttl);

        await refreshSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        cache.Verify(c => c.SetValueAsync("key", "__null__", It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
    }

    // ──────────────────────────────────────────────────────────
    // SetAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_StoresSerializedValueWithJitteredTtl()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);

        var sut = new RefreshAheadCache(cache.Object);
        var baseTtl = TimeSpan.FromMinutes(5);

        await sut.SetAsync("key", new Item(3), baseTtl);

        cache.Verify(c => c.SetValueAsync(
            "key",
            It.Is<string>(s => s.Contains("3")),
            It.Is<TimeSpan?>(t => t.HasValue && t.Value > TimeSpan.Zero)),
            Times.Once);
    }

    // ──────────────────────────────────────────────────────────
    // RemoveAsync
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_CallsDeleteOnCache()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.DeleteKeyAsync("key")).ReturnsAsync(true);

        var sut = new RefreshAheadCache(cache.Object);

        await sut.RemoveAsync("key");

        cache.Verify(c => c.DeleteKeyAsync("key"), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_ClearsExpiryTracker_SoNoRefreshTriggersAfterwards()
    {
        var ttl = TimeSpan.FromMilliseconds(50);
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.DeleteKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
        cache.Setup(c => c.GetValueAsync("key"))
            .ReturnsAsync(JsonSerializer.Serialize(new Item(1)));

        var sut = new RefreshAheadCache(cache.Object);
        await sut.SetAsync("key", new Item(1), ttl);
        await sut.RemoveAsync("key");

        // Wait past the threshold window
        await Task.Delay(45);

        var factoryCalled = false;
        await sut.GetOrSetAsync(
            "key",
            () =>
            {
                factoryCalled = true;
                return Task.FromResult<Item?>(new Item(2));
            },
            ttl);

        // Give a moment for any would-be background task to fire
        await Task.Delay(100);
        factoryCalled.Should().BeFalse("expiry tracker was cleared by RemoveAsync so no refresh should trigger");
    }

    private static async Task WaitUntilWithinRefreshWindowAsync(
        RefreshAheadCache sut,
        string key,
        TimeSpan ttl,
        TimeSpan? timeout = null)
    {
        var expiryTicksField = typeof(RefreshAheadCache).GetField("_expiryTicks", BindingFlags.Instance | BindingFlags.NonPublic);
        expiryTicksField.Should().NotBeNull();

        var expiryTicks = (System.Collections.Concurrent.ConcurrentDictionary<string, long>)expiryTicksField!.GetValue(sut)!;
        var thresholdMs = ttl.TotalMilliseconds * 0.2;
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));

        while (DateTime.UtcNow < deadline)
        {
            if (expiryTicks.TryGetValue(key, out var expiryTick))
            {
                var remainingMs = (new DateTime(expiryTick, DateTimeKind.Utc) - DateTime.UtcNow).TotalMilliseconds;
                if (remainingMs <= thresholdMs)
                    return;
            }

            await Task.Delay(5);
        }

        throw new TimeoutException($"Cache key '{key}' did not enter the refresh-ahead window in time.");
    }
}
