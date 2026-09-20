using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;

using backend.main.application.security;
using backend.main.features.cache;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using StackExchange.Redis;

namespace backend.tests.Unit.Application.Security;

public class RateLimiterConfigurationTests
{
    [Fact]
    public async Task AddInMemoryRateLimiter_ShouldRejectRequestsBeyondPermitLimit()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryRateLimiter();

        using var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        app.UseRateLimiter();
        app.Run(async context => await context.Response.WriteAsync("ok"));
        var pipeline = app.Build();

        for (var i = 0; i < 100; i++)
        {
            var allowedContext = CreateRateLimitContext(provider, IPAddress.Parse("127.0.0.1"));
            await pipeline(allowedContext);
            allowedContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        }

        var rejectedContext = CreateRateLimitContext(provider, IPAddress.Parse("127.0.0.1"));
        await pipeline(rejectedContext);

        rejectedContext.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        rejectedContext.Response.ContentType.Should().StartWith("application/json");
        rejectedContext.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(rejectedContext.Response.Body);
        json.RootElement.GetProperty("message").GetString()
            .Should().Be("Rate limit exceeded. Please try again later.");
        json.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("TOO_MANY_REQUESTS");
    }

    [Fact]
    public void GetPartitionKey_ShouldPreferAuthenticatedUserId_AndFallbackToIp()
    {
        var authenticatedContext = new DefaultHttpContext();
        authenticatedContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        authenticatedContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "42")],
                "Bearer"));

        var anonymousContext = new DefaultHttpContext();
        anonymousContext.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");

        InvokeGetPartitionKey(authenticatedContext).Should().Be("user:42");
        InvokeGetPartitionKey(anonymousContext).Should().Be("ip:10.1.2.3");
    }

    [Fact]
    public async Task AddTokenBucketRateLimit_ShouldAllowRequest_AndPopulateHeaders()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.EvalAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>()))
            .ReturnsAsync(new[] { 1, 4 });

        var provider = BuildRateLimitProvider(services =>
        {
            services.AddSingleton(cache.Object);
            services.AddTokenBucketRateLimit(
                new TokenBucketOptions(5, 2, 1, TimeSpan.FromMinutes(1)));
        });

        var app = new ApplicationBuilder(provider);
        app.UseTokenBucketRateLimit();
        app.Run(async context => await context.Response.WriteAsync("ok"));
        var pipeline = app.Build();

        var context = CreateRateLimitContext(provider, IPAddress.Parse("127.0.0.1"));
        await pipeline(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Headers["X-RateLimit-Limit"].ToString().Should().Be("5");
        context.Response.Headers["X-RateLimit-Remaining"].ToString().Should().Be("4");
    }

    [Fact]
    public async Task AddTokenBucketRateLimit_ShouldRejectRequest_WhenRedisScriptDenies()
    {
        var cache = new Mock<ICacheService>();
        RedisKey[]? capturedKeys = null;
        cache.Setup(service => service.EvalAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>()))
            .Callback<string, RedisKey[], RedisValue[]>((_, keys, _) => capturedKeys = keys)
            .ReturnsAsync(new RedisResult[]
            {
                RedisResult.Create((RedisValue)0),
                RedisResult.Create((RedisValue)0)
            });

        var provider = BuildRateLimitProvider(services =>
        {
            services.AddSingleton(cache.Object);
            services.AddTokenBucketRateLimit(
                new TokenBucketOptions(3, 1, 1, TimeSpan.FromMinutes(1)));
        });

        var app = new ApplicationBuilder(provider);
        app.UseTokenBucketRateLimit();
        app.Run(_ => Task.CompletedTask);
        var pipeline = app.Build();

        var context = CreateRateLimitContext(provider, IPAddress.Parse("127.0.0.1"));
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("sub", "user-123")],
                "Bearer"));

        await pipeline(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        (await reader.ReadToEndAsync()).Should().Be("Rate limit exceeded.");
        capturedKeys.Should().NotBeNull();
        capturedKeys!.Select(key => key.ToString()).Should().Contain([
            "rl:tb:user-123:t",
            "rl:tb:user-123:ts"
        ]);
    }

    [Fact]
    public async Task AddSlidingWindowRateLimit_ShouldAllowRequest_AndSetRemainingHeader()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.EvalAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>()))
            .ReturnsAsync(new[] { 1, 2 });

        var provider = BuildRateLimitProvider(services =>
        {
            services.AddSingleton(cache.Object);
            services.AddSlidingWindowRateLimit(
                new SlidingWindowOptions(5, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)));
        });

        var app = new ApplicationBuilder(provider);
        app.UseSlidingWindowRateLimit();
        app.Run(async context => await context.Response.WriteAsync("ok"));
        var pipeline = app.Build();

        var context = CreateRateLimitContext(provider, IPAddress.Parse("127.0.0.1"));
        await pipeline(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Headers["X-RateLimit-Limit"].ToString().Should().Be("5");
        context.Response.Headers["X-RateLimit-Remaining"].ToString().Should().Be("3");
    }

    [Fact]
    public async Task AddSlidingWindowRateLimit_ShouldRejectRequest_WhenLimitReached()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.EvalAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>()))
            .ReturnsAsync(new RedisResult[]
            {
                RedisResult.Create((RedisValue)0),
                RedisResult.Create((RedisValue)5)
            });

        var provider = BuildRateLimitProvider(services =>
        {
            services.AddSingleton(cache.Object);
            services.AddSlidingWindowRateLimit(
                new SlidingWindowOptions(5, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)));
        });

        var app = new ApplicationBuilder(provider);
        app.UseSlidingWindowRateLimit();
        app.Run(_ => Task.CompletedTask);
        var pipeline = app.Build();

        var context = CreateRateLimitContext(provider, IPAddress.Parse("127.0.0.1"));
        await pipeline(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        context.Response.Headers["X-RateLimit-Remaining"].ToString().Should().Be("0");
    }

    [Fact]
    public async Task ImageUploadPolicy_ShouldRejectRequestsBeyondPermitLimit()
    {
        // The limiter is not mounted under Testing (Program.cs skips UseRateLimiter there), so the
        // avatar endpoint's 429 cannot be observed by an integration test. Drive the real policy
        // in process instead, with endpoint metadata so the named policy runs rather than only the
        // global partition.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryRateLimiter();

        using var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        app.UseRateLimiter();
        app.Run(async context => await context.Response.WriteAsync("ok"));
        var pipeline = app.Build();

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new EnableRateLimitingAttribute(RateLimiterConfiguration.ImageUploadPolicyName)),
            "POST /api/profile/avatar");

        // Thirty uploads per ten minutes, partitioned per account.
        for (var i = 0; i < 30; i++)
        {
            var allowedContext = CreateAvatarUploadContext(provider, endpoint, "77");
            await pipeline(allowedContext);
            allowedContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        }

        var rejectedContext = CreateAvatarUploadContext(provider, endpoint, "77");
        await pipeline(rejectedContext);

        rejectedContext.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        rejectedContext.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(rejectedContext.Response.Body);
        json.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("TOO_MANY_REQUESTS");

        // Thirty-one requests is well inside the global 100/minute allowance, and a second account
        // is still served, so the rejection came from this account's image-upload bucket.
        var otherUserContext = CreateAvatarUploadContext(provider, endpoint, "78");
        await pipeline(otherUserContext);
        otherUserContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    private static DefaultHttpContext CreateAvatarUploadContext(
        IServiceProvider provider,
        Endpoint endpoint,
        string userId)
    {
        var context = CreateRateLimitContext(provider, IPAddress.Parse("127.0.0.1"));
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                "Bearer"));
        context.SetEndpoint(endpoint);
        return context;
    }

    private static ServiceProvider BuildRateLimitProvider(Action<ServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<IMiddlewareFactory, MiddlewareFactory>();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateRateLimitContext(IServiceProvider provider, IPAddress ipAddress)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.Connection.RemoteIpAddress = ipAddress;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string InvokeGetPartitionKey(HttpContext context)
    {
        var method = typeof(RateLimiterConfiguration).GetMethod(
            "GetPartitionKey",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, [context])!;
    }
}
