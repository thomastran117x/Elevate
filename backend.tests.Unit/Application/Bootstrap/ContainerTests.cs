using System.Reflection;

using backend.main.application.bootstrap;
using backend.main.features.auth.captcha;
using backend.main.features.clubs.posts.search;
using backend.main.features.clubs.search;
using backend.main.features.events.invitations;
using backend.main.features.events.search;
using backend.main.infrastructure.elasticsearch;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace backend.tests.Unit.Application.Bootstrap;

public class ContainerTests
{
    [Fact]
    public void ResolveCaptchaProvider_ShouldHonorExplicitGoogleSetting()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Captcha:Provider"] = "google",
                ["Turnstile:Secret"] = "secret-value"
            })
            .Build();

        InvokeResolveCaptchaProvider(config).Should().Be("google");
    }

    [Fact]
    public void ResolveCaptchaProvider_ShouldHonorExplicitTurnstileSetting()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CAPTCHA_PROVIDER"] = "turnstile"
            })
            .Build();

        InvokeResolveCaptchaProvider(config).Should().Be("turnstile");
    }

    [Fact]
    public void ResolveCaptchaProvider_ShouldInferTurnstile_WhenSecretExists()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TURNSTILE_SECRET"] = "secret-value"
            })
            .Build();

        InvokeResolveCaptchaProvider(config).Should().Be("turnstile");
    }

    [Fact]
    public void ResolveCaptchaProvider_ShouldFallbackToGoogle_WhenTurnstileSecretMissing()
    {
        var config = new ConfigurationBuilder().Build();

        InvokeResolveCaptchaProvider(config).Should().Be("google");
    }

    [Fact]
    public void AddSearchInfrastructure_ShouldRegisterSearchServices_AndCircuitBreaker()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddSearchInfrastructure(config);

        services.Any(descriptor =>
            descriptor.ServiceType == typeof(ElasticsearchCircuitBreaker)).Should().BeTrue();
        services.Any(descriptor =>
            descriptor.ServiceType == typeof(IEventSearchService)
            && descriptor.ImplementationType?.Name == "EventSearchService").Should().BeTrue();
        services.Any(descriptor =>
            descriptor.ServiceType == typeof(IClubSearchService)
            && descriptor.ImplementationType?.Name == "ClubSearchService").Should().BeTrue();
        services.Any(descriptor =>
            descriptor.ServiceType == typeof(IClubPostSearchService)
            && descriptor.ImplementationType?.Name == "ClubPostSearchService").Should().BeTrue();
    }

    [Fact]
    public void AddApplicationServices_ShouldRegisterCoreServicesWithoutHostedServices_WhenDisabled()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);

        services.AddApplicationServices(config, includeHostedServices: false);

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(ICaptchaService));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(EventInvitationStatusConsumerOptions));
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddApplicationServices_ShouldResolveGoogleCaptcha_ByDefault()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddApplicationServices(config, includeHostedServices: false);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var captcha = scope.ServiceProvider.GetRequiredService<ICaptchaService>();

        captcha.Should().BeOfType<GoogleCaptchaService>();
    }

    [Fact]
    public void AddApplicationServices_ShouldResolveTurnstileCaptcha_WhenConfigured()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Captcha:Provider"] = "turnstile"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddApplicationServices(config, includeHostedServices: true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var captcha = scope.ServiceProvider.GetRequiredService<ICaptchaService>();

        captcha.Should().BeOfType<CloudflareTurnstileCaptchaService>();
        services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(EventInvitationStatusConsumer)).Should().BeTrue();
        services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType?.Name == "ElasticsearchIndexInitializationService").Should().BeTrue();
        services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType?.Name == "ClubVersionCleanupService").Should().BeTrue();
    }

    private static string InvokeResolveCaptchaProvider(IConfiguration configuration)
    {
        var method = typeof(Container).GetMethod(
            "ResolveCaptchaProvider",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, [configuration])!;
    }
}
