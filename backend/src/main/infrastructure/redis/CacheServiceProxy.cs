using backend.main.features.cache;

using StackExchange.Redis;

namespace backend.main.infrastructure.redis
{
    /// <summary>
    /// Proxies all <see cref="ICacheService"/> calls to the current cache implementation
    /// held by <see cref="RedisReconnectState"/>, so that when Redis reconnects the app
    /// automatically uses the Redis-backed cache without restart.
    /// </summary>
    public sealed class CacheServiceProxy : ICacheService
    {
        private readonly RedisReconnectState _state;

        public CacheServiceProxy(RedisReconnectState state)
        {
            _state = state;
        }

        private ICacheService Current => _state.Current;

        public Task<bool> SetValueAsync(string key, string value, TimeSpan? expiry = null) =>
            Current.SetValueAsync(key, value, expiry);

        public Task<string?> GetValueAsync(string key) =>
            Current.GetValueAsync(key);

        public Task<long> IncrementAsync(string key, long value = 1) =>
            Current.IncrementAsync(key, value);

        public Task<long> DecrementAsync(string key, long value = 1) =>
            Current.DecrementAsync(key, value);

        public Task<bool> HashSetAsync(string key, string field, string value) =>
            Current.HashSetAsync(key, field, value);

        public Task<string?> HashGetAsync(string key, string field) =>
            Current.HashGetAsync(key, field);

        public Task<Dictionary<string, string>> HashGetAllAsync(string key) =>
            Current.HashGetAllAsync(key);

        public Task<bool> HashDeleteAsync(string key, string field) =>
            Current.HashDeleteAsync(key, field);

        public Task<bool> SetAddAsync(string key, string value) =>
            Current.SetAddAsync(key, value);

        public Task<bool> SetRemoveAsync(string key, string value) =>
            Current.SetRemoveAsync(key, value);

        public Task<string[]> SetMembersAsync(string key) =>
            Current.SetMembersAsync(key);

        public Task<long> ListLeftPushAsync(string key, string value) =>
            Current.ListLeftPushAsync(key, value);

        public Task<long> ListRightPushAsync(string key, string value) =>
            Current.ListRightPushAsync(key, value);

        public Task<string?> ListLeftPopAsync(string key) =>
            Current.ListLeftPopAsync(key);

        public Task<string?> ListRightPopAsync(string key) =>
            Current.ListRightPopAsync(key);

        public Task<bool> DeleteKeyAsync(string key) =>
            Current.DeleteKeyAsync(key);

        public Task<bool> KeyExistsAsync(string key) =>
            Current.KeyExistsAsync(key);

        public Task<TimeSpan?> GetTTLAsync(string key) =>
            Current.GetTTLAsync(key);

        public Task<bool> SetExpiryAsync(string key, TimeSpan expiry) =>
            Current.SetExpiryAsync(key, expiry);

        public Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry) =>
            Current.AcquireLockAsync(key, value, expiry);

        public Task<bool> ReleaseLockAsync(string key, string value) =>
            Current.ReleaseLockAsync(key, value);

        public IServer GetServer() =>
            Current.GetServer();

        public IEnumerable<string> ScanKeys(IServer server, string pattern) =>
            Current.ScanKeys(server, pattern);

        public Task<Dictionary<string, string?>> GetManyAsync(IEnumerable<string> keys) =>
            Current.GetManyAsync(keys);

        public Task<object> EvalAsync(string script, RedisKey[] keys, RedisValue[] values) =>
            Current.EvalAsync(script, keys, values);
    }
}
