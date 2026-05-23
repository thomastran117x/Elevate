using StackExchange.Redis;
using Moq;

namespace backend.tests.Integration.Infrastructure;

public sealed class InMemoryCacheService : backend.main.features.cache.ICacheService
{
    private sealed class CacheEntry
    {
        public string? StringValue { get; set; }
        public Dictionary<string, string> HashValues { get; } = [];
        public HashSet<string> SetValues { get; } = [];
        public LinkedList<string> ListValues { get; } = [];
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _entries = [];

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public Task<bool> SetValueAsync(string key, string value, TimeSpan? expiry = null)
    {
        lock (_gate)
        {
            var entry = GetOrCreateEntry(key);
            entry.StringValue = value;
            entry.ExpiresAt = ResolveExpiry(expiry);
        }

        return Task.FromResult(true);
    }

    public Task<string?> GetValueAsync(string key)
    {
        lock (_gate)
        {
            return Task.FromResult(TryGetEntry(key, out var entry) ? entry.StringValue : null);
        }
    }

    public Task<long> IncrementAsync(string key, long value = 1)
    {
        lock (_gate)
        {
            var entry = GetOrCreateEntry(key);
            var current = long.TryParse(entry.StringValue, out var parsed) ? parsed : 0L;
            current += value;
            entry.StringValue = current.ToString();
            return Task.FromResult(current);
        }
    }

    public Task<long> DecrementAsync(string key, long value = 1) => IncrementAsync(key, -value);

    public Task<bool> HashSetAsync(string key, string field, string value)
    {
        lock (_gate)
        {
            var entry = GetOrCreateEntry(key);
            entry.HashValues[field] = value;
            return Task.FromResult(true);
        }
    }

    public Task<string?> HashGetAsync(string key, string field)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry) || !entry.HashValues.TryGetValue(field, out var value))
                return Task.FromResult<string?>(null);

            return Task.FromResult<string?>(value);
        }
    }

    public Task<Dictionary<string, string>> HashGetAllAsync(string key)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry))
                return Task.FromResult(new Dictionary<string, string>());

            return Task.FromResult(entry.HashValues.ToDictionary(pair => pair.Key, pair => pair.Value));
        }
    }

    public Task<bool> HashDeleteAsync(string key, string field)
    {
        lock (_gate)
        {
            return Task.FromResult(TryGetEntry(key, out var entry) && entry.HashValues.Remove(field));
        }
    }

    public Task<bool> SetAddAsync(string key, string value)
    {
        lock (_gate)
        {
            var entry = GetOrCreateEntry(key);
            return Task.FromResult(entry.SetValues.Add(value));
        }
    }

    public Task<bool> SetRemoveAsync(string key, string value)
    {
        lock (_gate)
        {
            return Task.FromResult(TryGetEntry(key, out var entry) && entry.SetValues.Remove(value));
        }
    }

    public Task<string[]> SetMembersAsync(string key)
    {
        lock (_gate)
        {
            return Task.FromResult(TryGetEntry(key, out var entry)
                ? entry.SetValues.ToArray()
                : []);
        }
    }

    public Task<long> ListLeftPushAsync(string key, string value)
    {
        lock (_gate)
        {
            var entry = GetOrCreateEntry(key);
            entry.ListValues.AddFirst(value);
            return Task.FromResult((long)entry.ListValues.Count);
        }
    }

    public Task<long> ListRightPushAsync(string key, string value)
    {
        lock (_gate)
        {
            var entry = GetOrCreateEntry(key);
            entry.ListValues.AddLast(value);
            return Task.FromResult((long)entry.ListValues.Count);
        }
    }

    public Task<string?> ListLeftPopAsync(string key)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry) || entry.ListValues.First == null)
                return Task.FromResult<string?>(null);

            var value = entry.ListValues.First.Value;
            entry.ListValues.RemoveFirst();
            return Task.FromResult<string?>(value);
        }
    }

    public Task<string?> ListRightPopAsync(string key)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry) || entry.ListValues.Last == null)
                return Task.FromResult<string?>(null);

            var value = entry.ListValues.Last.Value;
            entry.ListValues.RemoveLast();
            return Task.FromResult<string?>(value);
        }
    }

    public Task<bool> DeleteKeyAsync(string key)
    {
        lock (_gate)
        {
            return Task.FromResult(_entries.Remove(key));
        }
    }

    public Task<bool> KeyExistsAsync(string key)
    {
        lock (_gate)
        {
            return Task.FromResult(TryGetEntry(key, out _));
        }
    }

    public Task<TimeSpan?> GetTTLAsync(string key)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry) || entry.ExpiresAt is null)
                return Task.FromResult<TimeSpan?>(null);

            return Task.FromResult<TimeSpan?>(entry.ExpiresAt.Value - DateTimeOffset.UtcNow);
        }
    }

    public Task<bool> SetExpiryAsync(string key, TimeSpan expiry)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry))
                return Task.FromResult(false);

            entry.ExpiresAt = DateTimeOffset.UtcNow.Add(expiry);
            return Task.FromResult(true);
        }
    }

    public IEnumerable<string> ScanKeys(IServer server, string pattern)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(pattern) || pattern == "*")
                return _entries.Keys.ToArray();

            var prefix = pattern.EndsWith('*')
                ? pattern[..^1]
                : pattern;

            return _entries.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .ToArray();
        }
    }

    public Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry)
    {
        lock (_gate)
        {
            if (TryGetEntry(key, out _))
                return Task.FromResult(false);

            var entry = GetOrCreateEntry(key);
            entry.StringValue = value;
            entry.ExpiresAt = DateTimeOffset.UtcNow.Add(expiry);
            return Task.FromResult(true);
        }
    }

    public Task<bool> ReleaseLockAsync(string key, string value)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry) || entry.StringValue != value)
                return Task.FromResult(false);

            _entries.Remove(key);
            return Task.FromResult(true);
        }
    }

    public IServer GetServer() => Mock.Of<IServer>();

    public Task<Dictionary<string, string?>> GetManyAsync(IEnumerable<string> keys)
    {
        lock (_gate)
        {
            return Task.FromResult(keys.ToDictionary(key => key, key =>
            {
                return TryGetEntry(key, out var entry) ? entry.StringValue : null;
            }));
        }
    }

    public Task<object> EvalAsync(string script, RedisKey[] keys, RedisValue[] values)
    {
        return Task.FromResult<object>(1L);
    }

    private CacheEntry GetOrCreateEntry(string key)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new CacheEntry();
            _entries[key] = entry;
        }

        return entry;
    }

    private bool TryGetEntry(string key, out CacheEntry entry)
    {
        if (_entries.TryGetValue(key, out entry!))
        {
            if (entry.ExpiresAt is not null && entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _entries.Remove(key);
                entry = null!;
                return false;
            }

            return true;
        }

        entry = null!;
        return false;
    }

    private static DateTimeOffset? ResolveExpiry(TimeSpan? expiry) =>
        expiry is null ? null : DateTimeOffset.UtcNow.Add(expiry.Value);
}
