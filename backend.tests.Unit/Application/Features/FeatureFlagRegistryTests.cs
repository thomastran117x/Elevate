using backend.main.application.features;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace backend.tests.Unit.Application.Features;

public class FeatureFlagRegistryTests
{
    [Fact]
    public void Registry_ShouldRejectDuplicateKeys_IgnoringCase()
    {
        var act = () => new FeatureFlagRegistry(["events", "EVENTS"]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate feature flag key*");
    }

    [Fact]
    public void Registry_ShouldReturnParentLineage_InOrder()
    {
        var lineage = FeatureFlagRegistry.Instance.GetLineage(FeatureFlagKeys.EventsInvitations);

        lineage.Should().Equal(FeatureFlagKeys.Events, FeatureFlagKeys.EventsInvitations);
    }

    [Fact]
    public void Registry_ShouldTranslateKnownKeys_ToEnvironmentVariableNames()
    {
        FeatureFlagRegistry.Instance.ToEnvironmentVariableName(FeatureFlagKeys.ProfileAdmin)
            .Should().Be("FEATURE_PROFILE_ADMIN");
    }

    [Fact]
    public void Evaluator_ShouldDefaultMissingFlags_ToEnabled()
    {
        var evaluator = CreateEvaluator(new Dictionary<string, bool>());

        evaluator.IsEnabled(FeatureFlagKeys.SearchReindex).Should().BeTrue();
    }

    [Fact]
    public void Evaluator_ShouldDisableDescendants_WhenParentIsOff()
    {
        var evaluator = CreateEvaluator(new Dictionary<string, bool>
        {
            [FeatureFlagKeys.Events] = false,
            [FeatureFlagKeys.EventsInvitations] = true
        });

        evaluator.IsEnabled(FeatureFlagKeys.EventsInvitations).Should().BeFalse();
    }

    [Fact]
    public void Evaluator_ShouldRejectUnknownKeys()
    {
        var evaluator = CreateEvaluator(new Dictionary<string, bool>());

        var act = () => evaluator.IsEnabled("events.unknown");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown feature flag key*");
    }

    [Fact]
    public void Options_ShouldRejectUnknownConfiguredKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:events.unknown"] = "false"
            })
            .Build();

        var act = () => FeatureFlagsOptions.FromConfiguration(configuration, FeatureFlagRegistry.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown feature flag key*");
    }

    private static FeatureFlagEvaluator CreateEvaluator(IDictionary<string, bool> flags)
    {
        return new FeatureFlagEvaluator(
            Options.Create(new FeatureFlagsOptions
            {
                Flags = new Dictionary<string, bool>(flags)
            }),
            FeatureFlagRegistry.Instance);
    }
}
