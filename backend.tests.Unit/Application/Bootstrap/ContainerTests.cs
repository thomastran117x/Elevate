using System.Reflection;

using backend.main.application.bootstrap;
using backend.main.application.features;
using backend.main.features.auth.captcha;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.invitations;
using backend.main.features.clubs.posts.search;
using backend.main.features.clubs.search;
using backend.main.features.events.invitations;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.payment;
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
    public void AddSearchInfrastructure_ShouldRegisterSearchServices_AndCircuitBreaker_WhenSearchEnabled()
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
    public void AddSearchInfrastructure_ShouldRegisterDisabledSearchServices_WhenSearchDisabled()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:search"] = "false"
            })
            .Build();

        services.AddSearchInfrastructure(config);

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IEventSearchService)
            && descriptor.ImplementationType == typeof(DisabledEventSearchService));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IClubSearchService)
            && descriptor.ImplementationType == typeof(DisabledClubSearchService));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IClubPostSearchService)
            && descriptor.ImplementationType == typeof(DisabledClubPostSearchService));
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(ElasticsearchCircuitBreaker));
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
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IClubInvitationService)
            && descriptor.ImplementationType == typeof(ClubInvitationService));
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddApplicationServices_ShouldRegisterDisabledFeatureServices_WhenFeatureFlagsAreOff()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:clubs.follow"] = "false",
                ["FeatureFlags:events.invitations"] = "false",
                ["FeatureFlags:events.registration"] = "false",
                ["FeatureFlags:payment"] = "false",
                ["FeatureFlags:search.reindex"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);

        services.AddApplicationServices(config, includeHostedServices: true);

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IFollowService)
            && descriptor.ImplementationType == typeof(DisabledFollowService));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IEventInvitationService)
            && descriptor.ImplementationType == typeof(DisabledEventInvitationService));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IEventRegistrationService)
            && descriptor.ImplementationType == typeof(DisabledEventRegistrationService));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IPaymentService)
            && descriptor.ImplementationType == typeof(DisabledPaymentService));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IEventReindexService)
            && descriptor.ImplementationType == typeof(DisabledEventReindexService));
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(EventInvitationStatusConsumerOptions));
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(EventInvitationStatusConsumer));
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
