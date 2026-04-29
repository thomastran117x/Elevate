using backend.main.services.interfaces;
using Moq;

namespace backend.test.TestSupport;

internal sealed class InMemoryCacheState
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> Counters { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, HashSet<string>> Sets { get; } = new(StringComparer.Ordinal);
}

internal static class InMemoryCacheMock
{
    public static Mock<ICacheService> Create(InMemoryCacheState? state = null)
    {
        state ??= new InMemoryCacheState();
        var mock = new Mock<ICacheService>();

        mock.Setup(cache => cache.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => state.Values.TryGetValue(key, out var value) ? value : null);

        mock.Setup(cache => cache.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((string key, string value, TimeSpan? _) =>
            {
                state.Values[key] = value;
                return true;
            });

        mock.Setup(cache => cache.DeleteKeyAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) =>
            {
                var removedValue = state.Values.Remove(key);
                var removedCounter = state.Counters.Remove(key);
                var removedSet = state.Sets.Remove(key);
                return removedValue || removedCounter || removedSet;
            });

        mock.Setup(cache => cache.IncrementAsync(It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync((string key, long amount) =>
            {
                state.Counters.TryGetValue(key, out var current);
                current += amount;
                state.Counters[key] = current;
                return current;
            });

        mock.Setup(cache => cache.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        mock.Setup(cache => cache.SetAddAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string key, string value) =>
            {
                if (!state.Sets.TryGetValue(key, out var members))
                {
                    members = new HashSet<string>(StringComparer.Ordinal);
                    state.Sets[key] = members;
                }

                return members.Add(value);
            });

        mock.Setup(cache => cache.SetRemoveAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string key, string value) =>
            {
                if (!state.Sets.TryGetValue(key, out var members))
                    return false;

                var removed = members.Remove(value);
                if (members.Count == 0)
                    state.Sets.Remove(key);

                return removed;
            });

        mock.Setup(cache => cache.SetMembersAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) =>
            {
                if (!state.Sets.TryGetValue(key, out var members))
                    return Array.Empty<string>();

                return members.ToArray();
            });

        return mock;
    }
}
