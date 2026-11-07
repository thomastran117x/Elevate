using System.Security.Cryptography;
using System.Text;

using backend.main.features.cache;
using backend.main.shared.exceptions.http;

using Microsoft.AspNetCore.WebUtilities;

using Newtonsoft.Json;

using StackExchange.Redis;

namespace backend.main.features.auth.oauth.links
{
    public sealed class ProviderLinkStateStore : IProviderLinkStateStore
    {
        // GET then DEL in one step, so of concurrent completions presenting one state exactly one
        // receives it.
        private const string ConsumeScript = @"
local value = redis.call('GET', KEYS[1])
if value then
    redis.call('DEL', KEYS[1])
end
return value";

        private readonly ICacheService _cache;

        public ProviderLinkStateStore(ICacheService cache)
        {
            _cache = cache;
        }

        public Task<string> CreateLinkStateAsync(ProviderLinkState state) =>
            CreateAsync(LinkStateKey, state.UserId, state);

        public Task<ProviderLinkState?> ConsumeLinkStateAsync(string stateToken) =>
            ConsumeAsync<ProviderLinkState>(LinkStateKey, stateToken);

        public Task<string> CreatePendingEmailLinkAsync(PendingEmailLink pending) =>
            CreateAsync(PendingEmailLinkKey, pending.UserId, pending);

        public Task<PendingEmailLink?> ConsumePendingEmailLinkAsync(string linkToken) =>
            ConsumeAsync<PendingEmailLink>(PendingEmailLinkKey, linkToken);

        public async Task RevokeAllForUserAsync(int userId)
        {
            var indexKey = UserIndexKey(userId);
            foreach (var key in await _cache.SetMembersAsync(indexKey))
                await _cache.DeleteKeyAsync(key);

            await _cache.DeleteKeyAsync(indexKey);
        }

        private async Task<string> CreateAsync<T>(Func<string, string> keyFor, int userId, T value)
        {
            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var key = keyFor(token);

            if (!await _cache.SetValueAsync(key, JsonConvert.SerializeObject(value), ProviderLinkStateLifetime.Lifetime))
                throw new NotAvailableException();

            // The index lets a link change revoke what is outstanding. It lives as long as the
            // newest entry; a member that expired first is deleted harmlessly on revocation.
            var indexKey = UserIndexKey(userId);
            await _cache.SetAddAsync(indexKey, key);
            await _cache.SetExpiryAsync(indexKey, ProviderLinkStateLifetime.Lifetime);

            return token;
        }

        private async Task<T?> ConsumeAsync<T>(Func<string, string> keyFor, string token) where T : class
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var read = await _cache.TryEvalAsync(ConsumeScript, [(RedisKey)keyFor(token)], []);
            if (!read.Succeeded)
                throw new NotAvailableException();

            var json = read.Value switch
            {
                RedisResult result when !result.IsNull => result.ToString(),
                string text => text,
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // Keys hold a hash of the token, so reading the cache does not yield a usable state.
        private static string LinkStateKey(string token) => $"oauth:link-state:{Hash(token)}";

        private static string PendingEmailLinkKey(string token) => $"oauth:pending-link:{Hash(token)}";

        private static string UserIndexKey(int userId) => $"oauth:link-index:user:{userId}";

        private static string Hash(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
