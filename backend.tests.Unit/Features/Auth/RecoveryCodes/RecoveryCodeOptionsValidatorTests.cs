using backend.main.features.auth.mfa.recovery;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth.RecoveryCodes;

public class RecoveryCodeOptionsValidatorTests
{
    private static readonly string ValidPepper = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public void Validate_ShouldRequireAPepper_OutsideDevelopmentAndTest()
    {
        var production = new RecoveryCodeOptionsValidator(isNonProduction: false);
        var development = new RecoveryCodeOptionsValidator(isNonProduction: true);

        production.Validate(null, new RecoveryCodeOptions()).Failed.Should().BeTrue();
        production.Validate(null, new RecoveryCodeOptions { Pepper = RecoveryCodeOptions.DevelopmentPepper })
            .Failed.Should().BeTrue();
        production.Validate(null, new RecoveryCodeOptions { Pepper = ValidPepper }).Succeeded.Should().BeTrue();
        development.Validate(null, new RecoveryCodeOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("c2hvcnQ=")]
    public void Validate_ShouldReject_AMalformedOrShortPepper(string pepper)
    {
        var result = new RecoveryCodeOptionsValidator(isNonProduction: true)
            .Validate(null, new RecoveryCodeOptions { Pepper = pepper });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Auth:RecoveryCodes:Pepper");
    }

    [Fact]
    public void DevelopmentPepper_ShouldMeetTheMinimumLength()
    {
        new RecoveryCodeOptions().PepperBytes().Length.Should().BeGreaterThanOrEqualTo(RecoveryCodeOptions.MinimumPepperBytes);
    }
}
