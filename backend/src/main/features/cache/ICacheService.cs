using StackExchange.Redis;

namespace backend.main.features.cache
{
    public interface ICacheService
    {
        Task<bool> SetValueAsync(string key, string value, TimeSpan? expiry = null);
        Task<string?> GetValueAsync(string key);
        Task<long> IncrementAsync(string key, long value = 1);
        Task<long> DecrementAsync(string key, long value = 1);
        Task<bool> HashSetAsync(string key, string field, string value);
        Task<string?> HashGetAsync(string key, string field);
        Task<Dictionary<string, string>> HashGetAllAsync(string key);
        Task<bool> HashDeleteAsync(string key, string field);
        Task<bool> SetAddAsync(string key, string value);
        Task<bool> SetRemoveAsync(string key, string value);
        Task<string[]> SetMembersAsync(string key);
        Task<long> ListLeftPushAsync(string key, string value);
        Task<long> ListRightPushAsync(string key, string value);
        Task<string?> ListLeftPopAsync(string key);
        Task<string?> ListRightPopAsync(string key);
        Task<bool> DeleteKeyAsync(string key);
        Task<bool> KeyExistsAsync(string key);
        Task<TimeSpan?> GetTTLAsync(string key);
        Task<bool> SetExpiryAsync(string key, TimeSpan expiry);
        IEnumerable<string> ScanKeys(IServer server, string pattern);
        Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry);
        Task<bool> ReleaseLockAsync(string key, string value);
        IServer GetServer();
        Task<Dictionary<string, string?>> GetManyAsync(IEnumerable<string> keys);

        /// <summary>
        /// Evaluates a Lua script on the cache (Redis). When Redis is unavailable,
        /// returns a result that allows the request (e.g. for rate limiters: allowed=1).
        /// </summary>
        /// <param name="script">Lua script.</param>
        /// <param name="keys">Redis keys.</param>
        /// <param name="values">Redis values (ARGV).</param>
        /// <returns>RedisResult from Redis, or an array of RedisResult when using no-op (allow-all).</returns>
        Task<object> EvalAsync(string script, RedisKey[] keys, RedisValue[] values);
    }
}
