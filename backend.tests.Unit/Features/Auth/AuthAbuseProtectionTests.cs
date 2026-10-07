using backend.main.features.auth.abuse;
using backend.main.features.cache;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using StackExchange.Redis;

namespace backend.tests.Unit.Features.Auth;

public sealed class AuthAbuseProtectionTests
{
    [Fact]
    public void OptionsValidator_ShouldRejectInvalidCrossFieldValues()
    {
        var options = new AuthAbuseProtectionOptions
        {
            SharedIpPermitLimit = 0,
            CaptchaFailureThreshold = 11,
            LoginAccountFailureLimit = 10,
            DelayBase = TimeSpan.FromSeconds(2),
            DelayMaximum = TimeSpan.FromSeconds(1),
        };

        var result = new AuthAbuseProtectionOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(message => message.Contains("SharedIpPermitLimit"));
        result.Failures.Should().Contain(message => message.Contains("CaptchaFailureThreshold"));
        result.Failures.Should().Contain(message => message.Contains("DelayMaximum"));
    }

    [Fact]
    public async Task SourceAndTargetCounters_ShouldUseIndependentHashedKeys()
    {
        var store = new RecordingStore();
        var service = CreateService(store);

        await service.EnsureSourceAllowedAsync(AuthAbuseFlow.Recovery, "192.0.2.10");
        await service.EnsureTargetAllowedAsync(
            AuthAbuseFlow.Recovery,
            AuthAbuseTargetKind.Email,
            " Ada@Example.com ");

        store.ReservedKeys.Should().HaveCount(2);
        store.ReservedKeys[0].Should().Contain(":shared:ip:");
        store.ReservedKeys[1].Should().Contain(":recovery:target:");
        store.ReservedKeys.Should().OnlyContain(key => !key.Contains("192.0.2.10"));
        store.ReservedKeys.Should().OnlyContain(key => !key.Contains("ada@example.com"));
    }

    [Fact]
    public async Task RedisStore_ShouldParseTheWrappedLuaArrayReturnedByStackExchangeRedis()
    {
        var cache = new Mock<ICacheService>();
        var luaResult = RedisResult.Create(
        [
            RedisResult.Create((RedisValue)1),
            RedisResult.Create((RedisValue)2),
            RedisResult.Create((RedisValue)12_000),
        ]);
        cache.Setup(service => service.TryEvalAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>()))
            .ReturnsAsync(new CacheScriptResult(true, luaResult));
        var store = new RedisAuthAbuseProtectionStore(cache.Object);

        var result = await store.ReserveAsync("auth:abuse:v1:test", 10, TimeSpan.FromMinutes(5));

        result.StoreAvailable.Should().BeTrue();
        result.Allowed.Should().BeTrue();
        result.Count.Should().Be(2);
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task LoginFailures_ShouldEscalateCaptchaAndReset()
    {
        var store = new RecordingStore
        {
            RecordResult = new AuthCounterResult(
                true,
                true,
                3,
                TimeSpan.FromMinutes(15))
        };
        var service = CreateService(store);

        var state = await service.RecordLoginFailureAsync(" ExampleUser ");
        await service.ResetLoginFailuresAsync("exampleuser");

        state.CaptchaRequired.Should().BeTrue();
        state.Throttled.Should().BeFalse();
        store.DeletedKeys.Should().ContainSingle();
        store.RecordedKeys.Single().Should().Be(store.DeletedKeys.Single());
    }

    [Fact]
    public void LoginDelay_ShouldGrowExponentiallyAndCapAtEightSeconds()
    {
        var service = CreateService(new RecordingStore());

        service.CalculateLoginDelay(0).Should().Be(TimeSpan.Zero);
        service.CalculateLoginDelay(1).Should().Be(TimeSpan.FromMilliseconds(500));
        service.CalculateLoginDelay(2).Should().Be(TimeSpan.FromSeconds(1));
        service.CalculateLoginDelay(3).Should().Be(TimeSpan.FromSeconds(2));
        service.CalculateLoginDelay(5).Should().Be(TimeSpan.FromSeconds(8));
        service.CalculateLoginDelay(20).Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task UnavailableRedis_ShouldFailOpenAndEmitCriticalOnlyOnce()
    {
        var store = new RecordingStore
        {
            ReserveResult = new AuthCounterResult(false, true, 0, null)
        };
        var logger = new Mock<ILogger<AuthAbuseProtectionService>>();
        var service = CreateService(store, logger.Object);

        await service.EnsureSourceAllowedAsync(AuthAbuseFlow.General, "192.0.2.10");
        await service.EnsureSourceAllowedAsync(AuthAbuseFlow.General, "192.0.2.10");
        store.ReserveResult = new AuthCounterResult(true, true, 1, TimeSpan.FromMinutes(5));
        await service.EnsureSourceAllowedAsync(AuthAbuseFlow.General, "192.0.2.10");

        logger.VerifyLog(LogLevel.Critical, Times.Once());
        logger.VerifyLog(LogLevel.Information, Times.Once());
    }

    [Fact]
    public async Task ThrottledDecision_ShouldReturnGenericExceptionAndRetryAfter()
    {
        var store = new RecordingStore
        {
            ReserveResult = new AuthCounterResult(
                true,
                false,
                10,
                TimeSpan.FromSeconds(12))
        };
        var context = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = context };
        var service = CreateService(store, httpContextAccessor: accessor);

        var action = () => service.EnsureSourceAllowedAsync(
            AuthAbuseFlow.General,
            "192.0.2.10");

        await action.Should().ThrowAsync<TooManyRequestException>()
            .WithMessage("Too many requests. Please try again later.");
        context.Response.Headers.RetryAfter.ToString().Should().Be("12");
    }

    private static AuthAbuseProtectionService CreateService(
        IAuthAbuseProtectionStore store,
        ILogger<AuthAbuseProtectionService>? logger = null,
        IHttpContextAccessor? httpContextAccessor = null) =>
        new(
            store,
            Options.Create(new AuthAbuseProtectionOptions()),
            TimeProvider.System,
            httpContextAccessor ?? new HttpContextAccessor(),
            logger ?? Mock.Of<ILogger<AuthAbuseProtectionService>>());

    private sealed class RecordingStore : IAuthAbuseProtectionStore
    {
        public AuthCounterResult ReserveResult { get; set; } =
            new(true, true, 1, TimeSpan.FromMinutes(5));

        public AuthCounterResult ReadResult { get; set; } =
            new(true, true, 0, null);

        public AuthCounterResult RecordResult { get; set; } =
            new(true, true, 1, TimeSpan.FromMinutes(15));

        public List<string> ReservedKeys { get; } = [];
        public List<string> RecordedKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];

        public Task<AuthCounterResult> ReserveAsync(
            string key,
            int limit,
            TimeSpan window,
            CancellationToken cancellationToken = default)
        {
            ReservedKeys.Add(key);
            return Task.FromResult(ReserveResult);
        }

        public Task<AuthCounterResult> ReadAsync(
            string key,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReadResult);

        public Task<AuthCounterResult> RecordFailureAsync(
            string key,
            int limit,
            TimeSpan window,
            CancellationToken cancellationToken = default)
        {
            RecordedKeys.Add(key);
            return Task.FromResult(RecordResult);
        }

        public Task<bool> DeleteAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);
            return Task.FromResult(true);
        }
    }
}

internal static class LoggerVerificationExtensions
{
    public static void VerifyLog<T>(
        this Mock<ILogger<T>> logger,
        LogLevel level,
        Times times)
    {
        logger.Verify(
            value => value.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }
}
