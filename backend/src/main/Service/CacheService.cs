
using backend.main.Interfaces;
using backend.main.Resources;

using StackExchange.Redis;

namespace backend.main.Services
{
    public class CacheService : BaseCacheService, ICacheService
    {
        public CacheService(
            RedisResource redisResource
        ) : base(redisResource) { }

        public Task<bool> SetValueAsync(string key, string value, TimeSpan? expiry = null) =>
            ExecuteAsync(() => _db.StringSetAsync(key, value, expiry), false);

        public Task<string?> GetValueAsync(string key) =>
            ExecuteAsync(async () =>
            {
                var v = await _db.StringGetAsync(key);
                return v.HasValue ? v.ToString() : null;
            });

        public Task<long> IncrementAsync(string key, long value = 1) =>
            ExecuteAsync(() => _db.StringIncrementAsync(key, value), 0);

        public Task<long> DecrementAsync(string key, long value = 1) =>
            ExecuteAsync(() => _db.StringDecrementAsync(key, value), 0);

        public Task<bool> HashSetAsync(string key, string field, string value) =>
            ExecuteAsync(() => _db.HashSetAsync(key, field, value), false);

        public Task<string?> HashGetAsync(string key, string field) =>
            ExecuteAsync(async () =>
            {
                var v = await _db.HashGetAsync(key, field);
                return v.HasValue ? v.ToString() : null;
            });

        public Task<Dictionary<string, string>> HashGetAllAsync(string key) =>
            ExecuteAsync(async () =>
            {
                var entries = await _db.HashGetAllAsync(key);
                return entries.ToDictionary(
                    e => e.Name.ToString(),
                    e => e.Value.ToString()
                );
            }, new Dictionary<string, string>());

        public Task<bool> HashDeleteAsync(string key, string field) =>
            ExecuteAsync(() => _db.HashDeleteAsync(key, field), false);

        public Task<bool> SetAddAsync(string key, string value) =>
            ExecuteAsync(() => _db.SetAddAsync(key, value), false);

        public Task<bool> SetRemoveAsync(string key, string value) =>
            ExecuteAsync(() => _db.SetRemoveAsync(key, value), false);

        public Task<string[]> SetMembersAsync(string key) =>
            ExecuteAsync(async () =>
            {
                var members = await _db.SetMembersAsync(key);
                return members.Select(m => m.ToString()).ToArray();
            }, Array.Empty<string>());

        public Task<long> ListLeftPushAsync(string key, string value) =>
            ExecuteAsync(() => _db.ListLeftPushAsync(key, value), 0);

        public Task<long> ListRightPushAsync(string key, string value) =>
            ExecuteAsync(() => _db.ListRightPushAsync(key, value), 0);

        public Task<string?> ListLeftPopAsync(string key) =>
            ExecuteAsync(async () =>
            {
                var v = await _db.ListLeftPopAsync(key);
                return v.HasValue ? v.ToString() : null;
            });

        public Task<string?> ListRightPopAsync(string key) =>
            ExecuteAsync(async () =>
            {
                var v = await _db.ListRightPopAsync(key);
                return v.HasValue ? v.ToString() : null;
            });

        public Task<bool> DeleteKeyAsync(string key) =>
            ExecuteAsync(() => _db.KeyDeleteAsync(key), false);

        public Task<bool> KeyExistsAsync(string key) =>
            ExecuteAsync(() => _db.KeyExistsAsync(key), false);

        public Task<TimeSpan?> GetTTLAsync(string key) =>
            ExecuteAsync(() => _db.KeyTimeToLiveAsync(key));

        public Task<bool> SetExpiryAsync(string key, TimeSpan expiry) =>
            ExecuteAsync(() => _db.KeyExpireAsync(key, expiry), false);

        public Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry) =>
            ExecuteAsync(
                () => _db.StringSetAsync(key, value, expiry, When.NotExists),
                false
            );

        public Task<bool> ReleaseLockAsync(string key, string value) =>
            ExecuteAsync(async () =>
            {
                var existing = await GetValueAsync(key);
                if (existing != value)
                    return false;

                return await DeleteKeyAsync(key);
            }, false);

        public IServer GetServer()
        {
            var endpoint = _redis.GetEndPoints().First();
            return _redis.GetServer(endpoint);
        }

        public IEnumerable<string> ScanKeys(IServer server, string pattern)
        {
            foreach (var key in server.Keys(pattern: pattern))
                yield return key.ToString();
        }

        public Task<Dictionary<string, string?>> GetManyAsync(IEnumerable<string> keys) =>
            ExecuteAsync(async () =>
            {
                var keyArr = keys.Select(k => (RedisKey)k).ToArray();

                if (keyArr.Length == 0)
                    return new Dictionary<string, string?>();

                var values = await _db.StringGetAsync(keyArr);

                var result = new Dictionary<string, string?>(keyArr.Length);

                for (int i = 0; i < keyArr.Length; i++)
                {
                    result[keyArr[i]!] = values[i].HasValue
                        ? values[i].ToString()
                        : null;
                }

                return result;
            }, new Dictionary<string, string?>());
    }
}
