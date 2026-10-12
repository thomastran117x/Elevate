using System.ComponentModel.DataAnnotations;

using backend.main.features.auth.password;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace backend.tests.Unit.Features.Auth.Password;

public class PasswordPolicyAttributeTests
{
    private const string LengthMessage = "Use between 15 and 128 characters.";

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void ShouldEnforceTheDefaultLengthRange(int length, bool valid)
    {
        var result = Validate(new string('x', length));

        if (valid)
            result.Should().Be(ValidationResult.Success);
        else
            result!.ErrorMessage.Should().Be(LengthMessage);
    }

    [Theory]
    [InlineData("all lowercase words")]
    [InlineData("ALL UPPERCASE WORDS")]
    [InlineData("no digits or symbols at all")]
    [InlineData("  spaces   count   too  ")]
    public void ShouldNotImposeCompositionRules(string password)
    {
        Validate(password).Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ShouldCountEmojiAndCombiningMarksAsOneCharacterEach()
    {
        // 14 emoji are 28 UTF-16 units but only 14 characters.
        Validate(string.Concat(Enumerable.Repeat("🔐", 14)))!.ErrorMessage.Should().Be(LengthMessage);
        Validate(string.Concat(Enumerable.Repeat("🔐", 15))).Should().Be(ValidationResult.Success);

        // "e" + combining acute normalizes to one "é".
        Validate(string.Concat(Enumerable.Repeat("é", 14)))!.ErrorMessage.Should().Be(LengthMessage);
    }

    [Fact]
    public void ShouldLeaveMissingValuesToRequired()
    {
        Validate(null).Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ShouldUseConfiguredBounds_WhenOptionsAreRegistered()
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new PasswordPolicyOptions { MinLength = 20, MaxLength = 64 }))
            .BuildServiceProvider();
        var context = new ValidationContext(new object(), services, null);

        new PasswordPolicyAttribute().GetValidationResult(new string('x', 19), context)!
            .ErrorMessage.Should().Be("Use between 20 and 64 characters.");
        new PasswordPolicyAttribute().GetValidationResult(new string('x', 20), context)
            .Should().Be(ValidationResult.Success);
    }

    private static ValidationResult? Validate(string? value) =>
        new PasswordPolicyAttribute().GetValidationResult(value, new ValidationContext(new object()));
}
