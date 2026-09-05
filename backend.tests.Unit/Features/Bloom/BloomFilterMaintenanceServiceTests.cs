using backend.main.features.bloom;
using backend.main.features.cache;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Features.Bloom;

public class BloomFilterMaintenanceServiceTests
{
    private const string Target = BloomFilterTargets.Username;

    /// <summary>
    /// Hydration runs before the filter answers anything, so a cold start degrades throughput
    /// rather than correctness.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ShouldHydrateOnStartup()
    {
        var cache = CreateCache();
        var registry = CreateRegistry(cache);
        using var service = CreateService(registry, cache, out _);
        using var cancellation = new CancellationTokenSource();

        await service.StartAsync(cancellation.Token);
        await WaitUntilAsync(() => registry.IsReady(Target));
        await service.StopAsync(CancellationToken.None);

        registry.IsReady(Target).Should().BeTrue();
        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.PossiblyPresent);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldKeepRunning_WhenTheRebuildFails()
    {
        var cache = CreateCache();
        var registry = CreateRegistry(cache);
        using var service = CreateService(registry, cache, out _, throwOnEnumerate: true);

        var act = async () =>
        {
            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_ShouldShutDownPromptly()
    {
        var cache = CreateCache();
        var registry = CreateRegistry(cache);
        using var service = CreateService(registry, cache, out _);

        await service.StartAsync(CancellationToken.None);
        var stop = service.StopAsync(CancellationToken.None);

        await stop.WaitAsync(TimeSpan.FromSeconds(10));
        stop.IsCompletedSuccessfully.Should().BeTrue();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(20);
        }

        throw new TimeoutException("Condition was not met before the deadline.");
    }

    private static BloomFilterMaintenanceService CreateService(
        BloomFilterRegistry registry,
        Mock<ICacheService> cache,
        out ServiceProvider provider,
        bool throwOnEnumerate = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(registry);
        services.AddSingleton(cache.Object);
        services.AddSingleton(BuildOptions());
        services.AddSingleton<IBloomFilterSource>(
            throwOnEnumerate ? new ThrowingSource() : new StubSource("ada"));
        services.AddScoped<BloomFilterRebuildRunner>();

        provider = services.BuildServiceProvider();

        return new BloomFilterMaintenanceService(provider, registry, TimeProvider.System, BuildOptions());
    }

    private static BloomFilterRegistry CreateRegistry(Mock<ICacheService> cache) =>
        new(cache.Object, BuildOptions(), TimeProvider.System);

    private static IOptions<BloomFilterOptions> BuildOptions() =>
        Options.Create(new BloomFilterOptions
        {
            RefreshIntervalSeconds = 5,
            Targets = new Dictionary<string, BloomFilterTargetOptions>(StringComparer.Ordinal)
            {
                [Target] = new() { ExpectedItems = 1000, FalsePositiveRate = 0.01 },
            },
        });

    private static Mock<ICacheService> CreateCache()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync(It.IsAny<string>())).ReturnsAsync("1");
        cache.Setup(c => c.GetBitmapAsync(It.IsAny<string>())).ReturnsAsync((byte[]?)null);
        cache.Setup(c => c.SetBitmapAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
        cache.Setup(c => c.SetMembersAsync(It.IsAny<string>())).ReturnsAsync([]);
        cache.Setup(c => c.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        return cache;
    }

    private sealed class StubSource(params string[] values) : IBloomFilterSource
    {
        public string Target => BloomFilterTargets.Username;

        public async IAsyncEnumerable<string> EnumerateAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return value;
                await Task.Yield();
            }
        }
    }

    private sealed class ThrowingSource : IBloomFilterSource
    {
        public string Target => BloomFilterTargets.Username;

        public async IAsyncEnumerable<string> EnumerateAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("database unavailable");
#pragma warning disable CS0162 // Unreachable: required to make this an iterator.
            yield break;
#pragma warning restore CS0162
        }
    }
}
