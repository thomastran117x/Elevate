using StackExchange.Redis;

namespace backend.main.features.cache
{
    /// <summary>
    /// No-op implementation of <see cref="ICacheService"/> used when Redis is unavailable.
    /// All operations complete without persisting; get operations return default/empty.
    /// </summary>
    public sealed class NoOpCacheService : ICacheService
    {
        public Task<bool> SetValueAsync(string key, string value, TimeSpan? expiry = null) =>
            Task.FromResult(false);

        public Task<string?> GetValueAsync(string key) =>
            Task.FromResult<string?>(null);

        public Task<long> IncrementAsync(string key, long value = 1) =>
            Task.FromResult(0L);

        public Task<long> DecrementAsync(string key, long value = 1) =>
            Task.FromResult(0L);

        public Task<bool> HashSetAsync(string key, string field, string value) =>
            Task.FromResult(false);

        public Task<string?> HashGetAsync(string key, string field) =>
            Task.FromResult<string?>(null);

        public Task<Dictionary<string, string>> HashGetAllAsync(string key) =>
            Task.FromResult(new Dictionary<string, string>());

        public Task<bool> HashDeleteAsync(string key, string field) =>
            Task.FromResult(false);

        public Task<bool> SetAddAsync(string key, string value) =>
            Task.FromResult(false);

        public Task<bool> SetRemoveAsync(string key, string value) =>
            Task.FromResult(false);

        public Task<string[]> SetMembersAsync(string key) =>
            Task.FromResult(Array.Empty<string>());

        public Task<long> ListLeftPushAsync(string key, string value) =>
            Task.FromResult(0L);

        public Task<long> ListRightPushAsync(string key, string value) =>
            Task.FromResult(0L);

        public Task<string?> ListLeftPopAsync(string key) =>
            Task.FromResult<string?>(null);

        public Task<string?> ListRightPopAsync(string key) =>
            Task.FromResult<string?>(null);

        public Task<bool> DeleteKeyAsync(string key) =>
            Task.FromResult(false);

        public Task<bool> KeyExistsAsync(string key) =>
            Task.FromResult(false);

        public Task<TimeSpan?> GetTTLAsync(string key) =>
            Task.FromResult<TimeSpan?>(null);

        public Task<bool> SetExpiryAsync(string key, TimeSpan expiry) =>
            Task.FromResult(false);

        public Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry) =>
            Task.FromResult(false);

        public Task<bool> ReleaseLockAsync(string key, string value) =>
            Task.FromResult(false);

        public IServer GetServer() =>
            throw new InvalidOperationException("Redis is not available. No server instance.");

        public IEnumerable<string> ScanKeys(IServer server, string pattern) =>
            Array.Empty<string>();

        public Task<Dictionary<string, string?>> GetManyAsync(IEnumerable<string> keys) =>
            Task.FromResult(new Dictionary<string, string?>());

        public Task<object> EvalAsync(string script, RedisKey[] keys, RedisValue[] values)
        {
            // Allow request when Redis is unavailable (e.g. rate limiters). Return int[] so middlewares can handle without RedisResult.
            return Task.FromResult((object)new int[] { 1, 0 });
        }
    }
}
