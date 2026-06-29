using System.Net;

using backend.main.application.security;

using FluentAssertions;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Application.Security;

public class SecurityConfigurationTests
{
    [Fact]
    public void AddCustomCors_ShouldUseConfiguredOrigins_AndExposeExpectedHeaders()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:Origins:0"] = "https://app.example.com",
                ["Cors:Origins:1"] = "https://admin.example.com"
            })
            .Build();

        services.AddCustomCors(config);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = options.GetPolicy(CorsConfiguration.DefaultPolicyName);

        policy.Should().NotBeNull();
        policy!.Origins.Should().Equal("https://app.example.com", "https://admin.example.com");
        policy.AllowAnyHeader.Should().BeTrue();
        policy.AllowAnyMethod.Should().BeTrue();
        policy.SupportsCredentials.Should().BeTrue();
        policy.ExposedHeaders.Should().Contain([
            "X-Request-Id",
            "X-RateLimit-Limit",
            "X-RateLimit-Remaining",
            "X-RateLimit-Reset"
        ]);
    }

    [Fact]
    public void AddCustomCors_ShouldFallbackToLocalhost_WhenOriginsMissing()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddCustomCors(config);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = options.GetPolicy(CorsConfiguration.DefaultPolicyName);

        policy.Should().NotBeNull();
        policy!.Origins.Should().Equal("http://localhost:3090");
    }

    [Fact]
    public void AddCustomCsrf_ShouldConfigureExpectedHeader_AndCookieSettings()
    {
        var services = new ServiceCollection();

        services.AddCustomCsrf();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        options.HeaderName.Should().Be(CsrfConfiguration.CsrfHeaderName);
        options.Cookie.Name.Should().Be(CsrfConfiguration.CsrfCookieName);
        options.Cookie.HttpOnly.Should().BeFalse();
        options.Cookie.SameSite.Should().Be(SameSiteMode.Strict);
        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.SameAsRequest);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/mfa/enroll/start")]
    [InlineData("/api/auth/mfa/enroll/verify")]
    [InlineData("/api/auth/mfa/enable/start")]
    [InlineData("/api/auth/mfa/disable")]
    [InlineData("/api/auth/mfa/remove")]
    [InlineData("/api/auth/mfa/sms/enroll/start")]
    [InlineData("/api/auth/mfa/sms/enroll/verify")]
    [InlineData("/api/auth/mfa/sms/enable/start")]
    [InlineData("/api/auth/mfa/sms/disable")]
    [InlineData("/api/auth/mfa/sms/remove")]
    [InlineData("/api/auth/mfa/totp/enroll/start")]
    [InlineData("/api/auth/mfa/totp/enroll/verify")]
    [InlineData("/api/auth/mfa/totp/enable")]
    [InlineData("/api/auth/mfa/totp/disable")]
    [InlineData("/api/auth/mfa/totp/remove")]
    [InlineData("/api/auth/mfa/verify/totp")]
    public async Task UseRefreshCsrfValidation_ShouldValidateProtectedPostRequests(string path)
    {
        var antiforgery = new Mock<IAntiforgery>();
        var services = new ServiceCollection()
            .AddSingleton(antiforgery.Object)
            .BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseRefreshCsrfValidation();
        app.Run(_ => Task.CompletedTask);

        var pipeline = app.Build();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;

        await pipeline(context);

        antiforgery.Verify(service => service.ValidateRequestAsync(context), Times.Once);
    }

    [Theory]
    [InlineData("GET", "/api/auth/login")]
    [InlineData("GET", "/api/auth/mfa/sms/disable")]
    [InlineData("POST", "/api/auth/mfa")]
    public async Task UseRefreshCsrfValidation_ShouldSkipValidation_ForUnprotectedRequests(string method, string path)
    {
        var antiforgery = new Mock<IAntiforgery>();
        var services = new ServiceCollection()
            .AddSingleton(antiforgery.Object)
            .BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseRefreshCsrfValidation();
        app.Run(_ => Task.CompletedTask);

        var pipeline = app.Build();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Request.Method = method;
        context.Request.Path = path;

        await pipeline(context);

        antiforgery.Verify(service => service.ValidateRequestAsync(It.IsAny<HttpContext>()), Times.Never);
    }

    [Fact]
    public void AddForwardedHeaders_ShouldParseConfiguredProxies_AndNetworks()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.1",
                ["ForwardedHeaders:KnownProxies:1"] = "invalid-ip",
                ["ForwardedHeaders:KnownNetworks:0"] = "192.168.0.0/24",
                ["ForwardedHeaders:KnownNetworks:1"] = "bad-network"
            })
            .Build();

        services.AddForwardedHeaders(config);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.ForwardedHeaders.Should().Be(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.ForwardLimit.Should().Be(1);
        options.RequireHeaderSymmetry.Should().BeTrue();
        options.KnownProxies.Should().ContainSingle(ip => ip.Equals(IPAddress.Parse("10.0.0.1")));
        options.KnownNetworks.Should().Contain(network =>
            network.Prefix.Equals(IPAddress.Parse("192.168.0.0"))
            && network.PrefixLength == 24);
    }

    [Fact]
    public void AddCustomRequestTimeouts_ShouldConfigureDefaultAndLongRunningPolicies()
    {
        var services = new ServiceCollection();

        services.AddCustomRequestTimeouts();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RequestTimeoutOptions>>().Value;

        options.DefaultPolicy.Should().NotBeNull();
        options.DefaultPolicy!.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        options.Policies.Should().ContainKey(RequestTimeoutConfiguration.LongRunning);
        options.Policies[RequestTimeoutConfiguration.LongRunning].Timeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task UseSecurityHeaders_ShouldAppendExpectedResponseHeaders()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseSecurityHeaders();
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        var pipeline = app.Build();
        var context = new DefaultHttpContext();

        await pipeline(context);

        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        context.Response.Headers["X-Permitted-Cross-Domain-Policies"].ToString().Should().Be("none");
        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
        context.Response.Headers["Permissions-Policy"].ToString()
            .Should().Contain("camera=()");
        context.Response.Headers["Content-Security-Policy"].ToString()
            .Should().Be("default-src 'none'; frame-ancestors 'none'");
    }

    [Fact]
    public void UseHttpsEnforcement_ShouldReturnAppUnchanged_InTestLikeEnvironments()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);

        var result = app.UseHttpsEnforcement();

        result.Should().BeSameAs(app);
    }
}
