using backend.main.features.auth;

using FluentAssertions;

using Microsoft.Extensions.Configuration;

namespace backend.tests.Unit.Features.Auth;

public class SeedAccountBypassPolicyTests
{
    private const string SeedEmail = "organizer@seed.eventxperience.test";
    private const string NonSeedEmail = "user@example.com";

    [Fact]
    public void IsBypassEnabledFor_ShouldBeFalse_InProduction_EvenWithFlagAndSeedEmail()
    {
        var policy = CreatePolicy(allowBypass: "true", environment: "production");

        policy.IsBypassEnabledFor(SeedEmail).Should().BeFalse();
    }

    [Fact]
    public void IsBypassEnabledFor_ShouldBeFalse_WhenFlagDisabled()
    {
        var policy = CreatePolicy(allowBypass: "false", environment: "development");

        policy.IsBypassEnabledFor(SeedEmail).Should().BeFalse();
    }

    [Fact]
    public void IsBypassEnabledFor_ShouldBeFalse_WhenFlagUnset()
    {
        var policy = CreatePolicy(allowBypass: null, environment: "development");

        policy.IsBypassEnabledFor(SeedEmail).Should().BeFalse();
    }

    [Fact]
    public void IsBypassEnabledFor_ShouldBeFalse_WhenEnvironmentUnset_EvenWithFlagAndSeedEmail()
    {
        // Production-safe default: no ENVIRONMENT/ASPNETCORE_ENVIRONMENT set (a prod
        // host relying on the framework default) must not fail open.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AUTH_SEED_ACCOUNT_BYPASS"] = "true"
            })
            .Build();

        var policy = new SeedAccountBypassPolicy(configuration);

        policy.IsBypassEnabledFor(SeedEmail).Should().BeFalse();
    }

    [Fact]
    public void IsBypassEnabledFor_ShouldBeFalse_ForNonSeedEmail()
    {
        var policy = CreatePolicy(allowBypass: "true", environment: "development");

        policy.IsBypassEnabledFor(NonSeedEmail).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsBypassEnabledFor_ShouldBeFalse_ForMissingEmail(string? email)
    {
        var policy = CreatePolicy(allowBypass: "true", environment: "development");

        policy.IsBypassEnabledFor(email).Should().BeFalse();
    }

    [Theory]
    [InlineData("development")]
    [InlineData("dev")]
    [InlineData("test")]
    [InlineData("testing")]
    [InlineData("Testing")]
    public void IsBypassEnabledFor_ShouldBeTrue_WhenFlagEnabled_NonProduction_AndSeedEmail(string environment)
    {
        var policy = CreatePolicy(allowBypass: "true", environment: environment);

        policy.IsBypassEnabledFor(SeedEmail).Should().BeTrue();
    }

    [Fact]
    public void IsBypassEnabledFor_ShouldMatchSeedDomain_CaseInsensitively()
    {
        var policy = CreatePolicy(allowBypass: "true", environment: "test");

        policy.IsBypassEnabledFor("Organizer@Seed.EventXperience.Test").Should().BeTrue();
    }

    private static SeedAccountBypassPolicy CreatePolicy(string? allowBypass, string environment)
    {
        var values = new Dictionary<string, string?>
        {
            ["ENVIRONMENT"] = environment
        };

        if (allowBypass != null)
        {
            values["AUTH_SEED_ACCOUNT_BYPASS"] = allowBypass;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new SeedAccountBypassPolicy(configuration);
    }
}
