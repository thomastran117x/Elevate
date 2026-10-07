using System.Net;
using System.Net.Http.Json;
using System.Collections.Concurrent;

using backend.main.application.security;
using backend.main.features.auth.abuse;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.utilities;
using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using StackExchange.Redis;

namespace backend.tests.Integration.Features.Auth;

[Collection(IntegrationTestCollection.Name)]
public sealed class DistributedAuthAbuseProtectionTests
{
    [Fact]
    public async Task AccountFailures_ShouldThrottleAcrossApiInstancesAndUseHashedKeys()
    {
        await using var app = await CreateRateLimitedAppAsync();
        using var peer = app.CreatePeerClient();
        await app.SeedUserAsync(
            "distributed-login@example.com",
            username: "distributed-login");

        (await LoginAsync(app, app.Client, "distributed-login", "wrong-1", "203.0.113.11"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync(app, peer, "DISTRIBUTED-LOGIN", "wrong-2", "203.0.113.12"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync(app, app.Client, " distributed-login ", "wrong-3", "203.0.113.13"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var throttled = await LoginAsync(
            app,
            peer,
            "distributed-login",
            "wrong-4",
            "203.0.113.14");

        throttled.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter.Should().NotBeNull();
        (await throttled.Content.ReadAsStringAsync()).ToLowerInvariant().Should()
            .NotContain("distributed-login");

        var keys = GetAbuseKeys(app);
        keys.Should().NotBeEmpty();
        keys.All(HasHashedSuffix).Should().BeTrue();
        keys.Should().OnlyContain(key =>
            !key.Contains("distributed-login", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SharedSourceBudget_ShouldThrottleOneIpSweepingManyAccounts()
    {
        await using var app = await CreateRateLimitedAppAsync(
            new Dictionary<string, string?>
            {
                ["Auth:AbuseProtection:SharedIpPermitLimit"] = "3",
                ["Auth:AbuseProtection:LoginAccountFailureLimit"] = "100",
                ["Auth:AbuseProtection:CaptchaFailureThreshold"] = "3",
            });
        using var peer = app.CreatePeerClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var client = attempt % 2 == 0 ? app.Client : peer;
            var response = await LoginAsync(
                app,
                client,
                $"swept-account-{attempt}",
                "wrong-password",
                "198.51.100.20");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var throttled = await LoginAsync(
            app,
            peer,
            "swept-account-4",
            "wrong-password",
            "198.51.100.20");
        throttled.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task SuccessfulLogin_ShouldResetFailureStateAcrossInstances()
    {
        await using var app = await CreateRateLimitedAppAsync();
        using var peer = app.CreatePeerClient();
        var user = await app.SeedUserAsync(
            "reset-login@example.com",
            username: "reset-login");
        await app.SeedKnownDeviceAsync(user.Id, "known-device");

        (await LoginAsync(app, app.Client, "reset-login", "wrong-1", "203.0.113.31"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync(app, app.Client, "reset-login", "wrong-2", "203.0.113.32"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var successful = await LoginAsync(
            app,
            peer,
            "reset-login",
            "Password123!",
            "203.0.113.33");
        successful.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await app.DescribeFailureAsync(successful));

        (await LoginAsync(app, app.Client, "reset-login", "wrong-3", "203.0.113.34"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync(app, peer, "reset-login", "wrong-4", "203.0.113.35"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Recovery_ShouldReturnTheSamePublicShapeForExistingAndUnknownTargetsAcrossInstances()
    {
        await using var app = await CreateRateLimitedAppAsync();
        using var peer = app.CreatePeerClient();
        await app.SeedUserAsync("recovery-existing@example.com", username: "recovery-existing");

        var existing = await PostWithSourceAsync(
            app,
            app.Client,
            "/api/auth/recovery/password",
            new PasswordRecoveryRequest
            {
                Username = "recovery-existing",
                Captcha = "test-captcha"
            },
            "203.0.113.41");
        var unknown = await PostWithSourceAsync(
            app,
            peer,
            "/api/auth/recovery/password",
            new PasswordRecoveryRequest
            {
                Username = "recovery-unknown",
                Captcha = "test-captcha"
            },
            "203.0.113.42");

        existing.StatusCode.Should().Be(HttpStatusCode.OK);
        unknown.StatusCode.Should().Be(existing.StatusCode);
        var existingBody = await app.ReadApiResponseAsync<VerificationChallengeResponse>(existing);
        var unknownBody = await app.ReadApiResponseAsync<VerificationChallengeResponse>(unknown);
        unknownBody.Message.Should().Be(existingBody.Message);
        unknownBody.Data.Should().NotBeNull();
        existingBody.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task EmailAvailabilityTargetBudget_ShouldBeSharedAcrossInstances()
    {
        await using var app = await CreateRateLimitedAppAsync(
            new Dictionary<string, string?>
            {
                ["Auth:AbuseProtection:EmailAvailabilityIpPermitLimit"] = "100",
                ["Auth:AbuseProtection:EmailAvailabilityTargetPermitLimit"] = "2",
            });
        using var peer = app.CreatePeerClient();

        (await CheckAvailabilityAsync(app, app.Client, "target@example.com", "203.0.113.51"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await CheckAvailabilityAsync(app, peer, " TARGET@example.com ", "203.0.113.52"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var throttled = await CheckAvailabilityAsync(
            app,
            app.Client,
            "target@example.com",
            "203.0.113.53");
        throttled.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task EmailAvailabilitySourceBudgetAndCaptcha_ShouldBeEnforced()
    {
        await using var app = await CreateRateLimitedAppAsync(
            new Dictionary<string, string?>
            {
                ["Auth:AbuseProtection:EmailAvailabilityIpPermitLimit"] = "2",
                ["Auth:AbuseProtection:EmailAvailabilityTargetPermitLimit"] = "100",
            });
        using var peer = app.CreatePeerClient();

        var missingCaptcha = await PostWithSourceAsync(
            app,
            app.Client,
            "/api/auth/email/availability",
            new { email = "captcha@example.com", captcha = "" },
            "198.51.100.60");
        missingCaptcha.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await CheckAvailabilityAsync(app, peer, "one@example.com", "198.51.100.60"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var throttled = await CheckAvailabilityAsync(
            app,
            app.Client,
            "two@example.com",
            "198.51.100.60");
        throttled.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task UnavailableSharedStore_ShouldRetainLocalIpLimiter()
    {
        var logs = new RecordingLogger<AuthAbuseProtectionService>();
        await using var app = await AuthApiTestApp.CreateAsync(
            services =>
            {
                services.RemoveAll<IAuthAbuseProtectionStore>();
                services.AddSingleton<IAuthAbuseProtectionStore, UnavailableStore>();
                services.AddSingleton<ILogger<AuthAbuseProtectionService>>(logs);
            },
            RateLimitedConfiguration());

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await LoginAsync(
                app,
                app.Client,
                $"local-fallback-{attempt}",
                "wrong-password",
                "192.0.2.50");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var throttled = await LoginAsync(
            app,
            app.Client,
            "local-fallback-throttled",
            "wrong-password",
            "192.0.2.50");
        throttled.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter.Should().NotBeNull();
        logs.Events.Count(entry =>
                entry.Level == LogLevel.Critical
                && entry.EventId.Id == 32001)
            .Should().Be(1);
    }

    private static Task<AuthApiTestApp> CreateRateLimitedAppAsync(
        IReadOnlyDictionary<string, string?>? additions = null)
    {
        var configuration = RateLimitedConfiguration();
        if (additions is not null)
        {
            foreach (var (key, value) in additions)
                configuration[key] = value;
        }

        return AuthApiTestApp.CreateAsync(configurationOverrides: configuration);
    }

    private static Dictionary<string, string?> RateLimitedConfiguration() => new()
    {
        ["Testing:EnableRateLimiter"] = "true",
        ["Auth:AbuseProtection:SharedIpPermitLimit"] = "100",
        ["Auth:AbuseProtection:LoginAccountFailureLimit"] = "3",
        ["Auth:AbuseProtection:CaptchaFailureThreshold"] = "3",
        ["Auth:AbuseProtection:DelayBase"] = "00:00:00.001",
        ["Auth:AbuseProtection:DelayMaximum"] = "00:00:00.001",
    };

    private static async Task<HttpResponseMessage> LoginAsync(
        AuthApiTestApp app,
        HttpClient client,
        string username,
        string password,
        string sourceIp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest
            {
                Username = username,
                Password = password,
                Captcha = "test-captcha",
                Transport = SessionTransportResolver.ApiValue,
            })
        };
        request.Headers.Add("X-Forwarded-For", sourceIp);
        request.Headers.Add(HttpUtility.TrustedDeviceHeaderName, "known-device");
        request.Headers.Add(
            CsrfConfiguration.CsrfHeaderName,
            await app.GetCsrfTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> CheckAvailabilityAsync(
        AuthApiTestApp app,
        HttpClient client,
        string email,
        string sourceIp) =>
        PostWithSourceAsync(
            app,
            client,
            "/api/auth/email/availability",
            new { email, captcha = "test-captcha" },
            sourceIp);

    private static async Task<HttpResponseMessage> PostWithSourceAsync(
        AuthApiTestApp app,
        HttpClient client,
        string path,
        object payload,
        string sourceIp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Forwarded-For", sourceIp);
        request.Headers.Add(
            CsrfConfiguration.CsrfHeaderName,
            await app.GetCsrfTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static IReadOnlyList<string> GetAbuseKeys(AuthApiTestApp app)
    {
        var connection = app.Services.GetRequiredService<IConnectionMultiplexer>();
        return connection.GetEndPoints()
            .Select(endpoint => connection.GetServer(endpoint))
            .Where(server => server.IsConnected)
            .SelectMany(server => server.Keys(app.ResourceSlot, "auth:abuse:v1:*"))
            .Select(key => key.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasHashedSuffix(string key)
    {
        var suffix = key.Substring(key.LastIndexOf(':') + 1);
        return suffix.Length == 64 && suffix.All(Uri.IsHexDigit);
    }

    private sealed class UnavailableStore : IAuthAbuseProtectionStore
    {
        private static readonly AuthCounterResult Unavailable = new(false, true, 0, null);

        public Task<AuthCounterResult> ReserveAsync(
            string key,
            int limit,
            TimeSpan window,
            CancellationToken cancellationToken = default) => Task.FromResult(Unavailable);

        public Task<AuthCounterResult> ReadAsync(
            string key,
            int limit,
            CancellationToken cancellationToken = default) => Task.FromResult(Unavailable);

        public Task<AuthCounterResult> RecordFailureAsync(
            string key,
            int limit,
            TimeSpan window,
            CancellationToken cancellationToken = default) => Task.FromResult(Unavailable);

        public Task<bool> DeleteAsync(
            string key,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<RecordedLog> Events { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Events.Enqueue(new RecordedLog(logLevel, eventId));
    }

    private sealed record RecordedLog(LogLevel Level, EventId EventId);
}
