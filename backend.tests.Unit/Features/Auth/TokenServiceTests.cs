using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using backend.main.features.auth.token;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth;

public class TokenServiceTests
{
    [Fact]
    public void GenerateAccessToken_ShouldIncludeIdentityRoleAndAuthVersionClaims()
    {
        var service = new TokenService(new InMemoryCacheService());
        var user = new User
        {
            Id = 23,
            Email = "claims@example.com",
            Usertype = "participant",
            AuthVersion = 7
        };

        var issue = service.GenerateAccessToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issue.Value);

        jwt.Claims.Should().Contain(claim => (claim.Type == ClaimTypes.NameIdentifier || claim.Type == "nameid") && claim.Value == "23");
        jwt.Claims.Should().Contain(claim => (claim.Type == ClaimTypes.Name || claim.Type == "unique_name") && claim.Value == "claims@example.com");
        jwt.Claims.Should().Contain(claim => (claim.Type == ClaimTypes.Role || claim.Type == "role") && claim.Value == "Participant");
        jwt.Claims.Should().Contain(claim => claim.Type == TokenService.AuthVersionClaimType && claim.Value == "7");
        issue.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(10));
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRejectTransportMismatch()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, requestInfo, SessionTransport.BrowserCookie);

        var act = () => service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.ApiToken,
            requestInfo);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh token transport mismatch.");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRejectMissingBindingToken()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, requestInfo, SessionTransport.BrowserCookie);

        var act = () => service.ValidateRefreshToken(
            issue.Value,
            null,
            SessionTransport.BrowserCookie,
            requestInfo);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Missing session binding token.");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRejectBindingTokenMismatch()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, requestInfo, SessionTransport.BrowserCookie);

        var act = () => service.ValidateRefreshToken(
            issue.Value,
            "wrong-binding",
            SessionTransport.BrowserCookie,
            requestInfo);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid session binding token.");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRejectReuseAfterSuccessfulValidation()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, requestInfo, SessionTransport.BrowserCookie);

        var result = await service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        result.UserId.Should().Be(8);

        var act = () => service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired refresh token.");
    }

    [Fact]
    public async Task VerifyVerificationOtpAsync_ShouldInvalidateChallengeAfterMaximumAttempts()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var user = new User
        {
            Email = "signup@example.com",
            Password = "hashed-password",
            Usertype = "Organizer"
        };

        var artifacts = await service.GenerateVerificationArtifactsAsync(user, VerificationPurpose.SignUp);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrongAttempt = () => service.VerifyVerificationOtpAsync(
                "000000",
                artifacts.OtpChallenge.Challenge,
                VerificationPurpose.SignUp);

            await wrongAttempt.Should().ThrowAsync<UnauthorizedException>();
        }

        var validAttempt = () => service.VerifyVerificationOtpAsync(
            artifacts.OtpChallenge.Code,
            artifacts.OtpChallenge.Challenge,
            VerificationPurpose.SignUp);

        await validAttempt.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired verification challenge.");
        (await service.VerificationTokenExist(user.Email, VerificationPurpose.SignUp)).Should().BeNull();
    }

    [Fact]
    public async Task GenerateAndValidateRefreshToken_ShouldPreserveUserAndTransport()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(14, requestInfo, SessionTransport.ApiToken);
        var result = await service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.ApiToken,
            requestInfo);

        result.UserId.Should().Be(14);
        result.Transport.Should().Be(SessionTransport.ApiToken);
        result.SessionId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GenerateRefreshToken_ShouldRejectUserMismatch_WhenReusingExistingSession()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, requestInfo, SessionTransport.BrowserCookie);
        var validation = await service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        var act = () => service.GenerateRefreshToken(
            9,
            requestInfo,
            SessionTransport.BrowserCookie,
            validation.SessionId);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh session user mismatch.");
    }

    [Fact]
    public async Task GenerateRefreshToken_ShouldRejectTransportMismatch_WhenReusingExistingSession()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, requestInfo, SessionTransport.BrowserCookie);
        var validation = await service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        var act = () => service.GenerateRefreshToken(
            8,
            requestInfo,
            SessionTransport.ApiToken,
            validation.SessionId);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh session transport mismatch.");
    }

    [Fact]
    public async Task GenerateVerificationArtifactsAsync_ShouldReuseExistingState_ForSameEmailAndPurpose()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var user = new User
        {
            Email = "signup@example.com",
            Password = "hashed-password",
            Usertype = "Organizer"
        };

        var first = await service.GenerateVerificationArtifactsAsync(user, VerificationPurpose.SignUp);
        var second = await service.GenerateVerificationArtifactsAsync(user, VerificationPurpose.SignUp);

        second.LinkToken.Should().Be(first.LinkToken);
        second.OtpChallenge.Challenge.Should().Be(first.OtpChallenge.Challenge);
        second.OtpChallenge.Code.Should().Be(first.OtpChallenge.Code);
        second.Purpose.Should().Be(VerificationPurpose.SignUp);
    }

    [Fact]
    public async Task VerifyVerificationToken_ShouldReturnUser_AndClearStoredState()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var user = new User
        {
            Email = "verify@example.com",
            Password = "hashed-password",
            Usertype = "Organizer"
        };

        var artifacts = await service.GenerateVerificationArtifactsAsync(user, VerificationPurpose.SignUp);
        var verifiedUser = await service.VerifyVerificationToken(
            artifacts.LinkToken,
            VerificationPurpose.SignUp);

        verifiedUser.Email.Should().Be("verify@example.com");
        verifiedUser.Password.Should().Be("hashed-password");
        verifiedUser.Usertype.Should().Be("Organizer");
        (await service.VerificationTokenExist("verify@example.com", VerificationPurpose.SignUp)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyVerificationToken_ShouldRejectPurposeMismatch()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var user = new User
        {
            Email = "verify@example.com",
            Password = "hashed-password",
            Usertype = "Organizer"
        };

        var artifacts = await service.GenerateVerificationArtifactsAsync(user, VerificationPurpose.SignUp);

        var act = () => service.VerifyVerificationToken(
            artifacts.LinkToken,
            VerificationPurpose.ResetPassword);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Verification token purpose mismatch.");
    }

    [Fact]
    public async Task RevokeAllRefreshSessionsAsync_ShouldInvalidateEverySessionForUser()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var browserRequest = CreateRequestInfo();
        var apiRequest = new ClientRequestInfo
        {
            IpAddress = "10.0.0.9",
            ClientName = "ApiClient",
            DeviceType = "Server",
            IsBrowserClient = false
        };

        var browserIssue = await service.GenerateRefreshToken(44, browserRequest, SessionTransport.BrowserCookie);
        var apiIssue = await service.GenerateRefreshToken(44, apiRequest, SessionTransport.ApiToken);

        await service.RevokeAllRefreshSessionsAsync(44);

        var browserAct = () => service.ValidateRefreshToken(
            browserIssue.Value,
            browserIssue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            browserRequest);
        var apiAct = () => service.ValidateRefreshToken(
            apiIssue.Value,
            apiIssue.SessionBindingToken,
            SessionTransport.ApiToken,
            apiRequest);

        await browserAct.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired refresh token.");
        await apiAct.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired refresh token.");
    }

    private static ClientRequestInfo CreateRequestInfo() => new()
    {
        IpAddress = "127.0.0.1",
        ClientName = "UnitTest",
        DeviceType = "Desktop",
        IsBrowserClient = true
    };
}

internal sealed class InMemoryCacheService : backend.main.features.cache.ICacheService
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

    public IEnumerable<string> ScanKeys(StackExchange.Redis.IServer server, string pattern)
    {
        lock (_gate)
        {
            return _entries.Keys.ToArray();
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

    public StackExchange.Redis.IServer GetServer() => throw new NotSupportedException();

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

    public Task<object> EvalAsync(string script, StackExchange.Redis.RedisKey[] keys, StackExchange.Redis.RedisValue[] values)
    {
        lock (_gate)
        {
            if (keys.Length == 1
                && values.Length == 2
                && script.Contains("redis.call('GET'", StringComparison.Ordinal)
                && script.Contains("redis.call('SET'", StringComparison.Ordinal))
            {
                var key = keys[0].ToString();
                var matchedWindow = long.Parse(values[0].ToString());
                var ttlMs = long.Parse(values[1].ToString());

                if (TryGetEntry(key, out var existing)
                    && long.TryParse(existing.StringValue, out var lastWindow)
                    && matchedWindow <= lastWindow)
                {
                    return Task.FromResult<object>(0L);
                }

                var entry = GetOrCreateEntry(key);
                entry.StringValue = matchedWindow.ToString();
                entry.ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(ttlMs);
                return Task.FromResult<object>(1L);
            }

            return Task.FromResult<object>(1L);
        }
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


