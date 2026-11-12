using System.Security.Cryptography;
using System.Text;

using backend.main.features.cache;
using backend.main.shared.exceptions.http;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

using Newtonsoft.Json;

using StackExchange.Redis;

namespace backend.main.features.auth.passkeys
{
    public enum PasskeyCeremonyType
    {
        Registration,
        SignIn,
        StepUp,
    }

    /// <summary>
    /// A ceremony between its options and its completion. It holds the options the browser was
    /// given, which carry the challenge, and what the completion must still match.
    /// </summary>
    public sealed class PasskeyCeremony
    {
        public PasskeyCeremonyType Type
        {
            get; set;
        }

        /// <summary>The WebAuthn options JSON the browser was given.</summary>
        public required string OptionsJson
        {
            get; set;
        }

        /// <summary>The account a registration or step-up belongs to; null for sign-in.</summary>
        public int? UserId
        {
            get; set;
        }

        /// <summary>The session that started a registration or step-up.</summary>
        public string? SessionId
        {
            get; set;
        }

        /// <summary>The account's auth version when the ceremony started.</summary>
        public int? AuthVersion
        {
            get; set;
        }

        /// <summary>The step-up generation captured when a step-up started.</summary>
        public long? StepUpGeneration
        {
            get; set;
        }

        /// <summary>The factor state captured when a step-up started.</summary>
        public string? FactorState
        {
            get; set;
        }

        /// <summary>The name a registration will give the new passkey.</summary>
        public string? FriendlyName
        {
            get; set;
        }

        public DateTime ExpiresAtUtc
        {
            get; set;
        }
    }

    public interface IPasskeyCeremonyStore
    {
        /// <summary>Stores the ceremony and returns the opaque id its completion presents.</summary>
        Task<string> CreateAsync(PasskeyCeremony ceremony);

        /// <summary>
        /// Takes the ceremony out of the store. Of concurrent completions presenting one id, at
        /// most one receives it, so a ceremony completes once whatever the outcome. Null when it
        /// is unknown, already used, expired, or of another type. Throws when the cache cannot
        /// answer rather than reporting the ceremony missing.
        /// </summary>
        Task<PasskeyCeremony?> ConsumeAsync(string ceremonyId, PasskeyCeremonyType expectedType);

        /// <summary>Cancels every ceremony an account has in flight.</summary>
        Task RevokeAllForUserAsync(int userId);
    }

    public sealed class PasskeyCeremonyStore : IPasskeyCeremonyStore
    {
        // GET then DEL in one step, so of concurrent completions presenting one id exactly one
        // receives the ceremony.
        private const string ConsumeScript = @"
local value = redis.call('GET', KEYS[1])
if value then
    redis.call('DEL', KEYS[1])
end
return value";

        private readonly ICacheService _cache;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _lifetime;

        public PasskeyCeremonyStore(ICacheService cache, TimeProvider timeProvider, IOptions<PasskeyOptions> options)
        {
            _cache = cache;
            _timeProvider = timeProvider;
            _lifetime = options.Value.CeremonyTimeout;
        }

        public async Task<string> CreateAsync(PasskeyCeremony ceremony)
        {
            var ceremonyId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var key = CeremonyKey(ceremonyId);
            ceremony.ExpiresAtUtc = _timeProvider.GetUtcNow().UtcDateTime.Add(_lifetime);

            if (!await _cache.SetValueAsync(key, JsonConvert.SerializeObject(ceremony), _lifetime))
                throw new NotAvailableException();

            // The index lets a factor change cancel what is in flight. It lives as long as the
            // newest entry; a member that expired first is deleted harmlessly on revocation.
            if (ceremony.UserId is { } userId)
            {
                var indexKey = UserIndexKey(userId);
                await _cache.SetAddAsync(indexKey, key);
                await _cache.SetExpiryAsync(indexKey, _lifetime);
            }

            return ceremonyId;
        }

        public async Task<PasskeyCeremony?> ConsumeAsync(string ceremonyId, PasskeyCeremonyType expectedType)
        {
            if (string.IsNullOrWhiteSpace(ceremonyId))
                return null;

            var read = await _cache.TryEvalAsync(ConsumeScript, [(RedisKey)CeremonyKey(ceremonyId)], []);
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

            PasskeyCeremony? ceremony;
            try
            {
                ceremony = JsonConvert.DeserializeObject<PasskeyCeremony>(json);
            }
            catch (JsonException)
            {
                return null;
            }

            // Redis expiry is only a backstop: the window is judged from the recorded expiry, so
            // a lingering key cannot stretch it. A ceremony of another type is spent all the same.
            if (ceremony is null
                || ceremony.Type != expectedType
                || DateTime.SpecifyKind(ceremony.ExpiresAtUtc, DateTimeKind.Utc) <= _timeProvider.GetUtcNow().UtcDateTime)
            {
                return null;
            }

            return ceremony;
        }

        public async Task RevokeAllForUserAsync(int userId)
        {
            var indexKey = UserIndexKey(userId);
            foreach (var key in await _cache.SetMembersAsync(indexKey))
                await _cache.DeleteKeyAsync(key);

            await _cache.DeleteKeyAsync(indexKey);
        }

        // Keys hold a hash of the id, so reading the cache does not yield a usable ceremony.
        private static string CeremonyKey(string ceremonyId) =>
            $"auth:passkey:ceremony:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ceremonyId)))}";

        private static string UserIndexKey(int userId) => $"auth:passkey:ceremony-index:user:{userId}";
    }
}
