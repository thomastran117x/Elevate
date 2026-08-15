using backend.main.features.profile;
using backend.main.shared.exceptions.http;

using FluentAssertions;

namespace backend.tests.Unit.Features.Profile;

public class UsernamePolicyTests
{
    [Theory]
    [InlineData("  Mixed.Case  ", "mixed.case")]
    [InlineData("already-lower", "already-lower")]
    [InlineData("\u00c9VÉNEMENT", "\u00e9vénement")]
    public void NormalizeAndValidate_ShouldTrimAndLowercase(string input, string expected)
    {
        UsernamePolicy.NormalizeAndValidate(input).Should().Be(expected);
    }

    [Fact]
    public void NormalizeAndValidate_ShouldRejectWhitespace()
    {
        var act = () => UsernamePolicy.NormalizeAndValidate("   ");

        act.Should().Throw<BadRequestException>()
            .WithMessage("Username is required.");
    }

    [Fact]
    public void NormalizeAndValidate_ShouldValidateTheNormalizedLength()
    {
        UsernamePolicy.NormalizeAndValidate($"  {new string('a', 50)}  ")
            .Should().HaveLength(50);

        var act = () => UsernamePolicy.NormalizeAndValidate(new string('a', 51));
        act.Should().Throw<BadRequestException>();
    }
}
