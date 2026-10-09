using backend.main.features.auth.mfa.session;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth;

public class StepUpOptionsValidatorTests
{
    [Fact]
    public void Defaults_ShouldBeTenMinutes_AndValid()
    {
        var options = new StepUpOptions();

        options.ProofLifetime.Should().Be(TimeSpan.FromMinutes(10));
        new StepUpOptionsValidator().Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void Validate_ShouldAccept_LifetimesWithinTheCap(int minutes)
    {
        var result = new StepUpOptionsValidator().Validate(
            null,
            new StepUpOptions { ProofLifetimeMinutes = minutes });

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(60)]
    public void Validate_ShouldReject_LifetimesOutsideOneToTenMinutes(int minutes)
    {
        var result = new StepUpOptionsValidator().Validate(
            null,
            new StepUpOptions { ProofLifetimeMinutes = minutes });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Auth:StepUp:ProofLifetimeMinutes");
    }
}
