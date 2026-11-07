using backend.main.features.auth.mfa.recovery;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth.RecoveryCodes;

public class RecoveryCodeFormatTests
{
    [Fact]
    public void GenerateSet_ShouldReturnTenDistinctCodesFromTheAlphabet()
    {
        var codes = RecoveryCodeFormat.GenerateSet();

        codes.Should().HaveCount(10).And.OnlyHaveUniqueItems();
        codes.Should().AllSatisfy(code =>
        {
            code.Should().HaveLength(RecoveryCodeFormat.Length);
            code.All(RecoveryCodeFormat.Alphabet.Contains).Should().BeTrue();
        });
    }

    [Fact]
    public void Entropy_ShouldBeAtLeast128BitsPerCode()
    {
        RecoveryCodeFormat.Alphabet.Distinct().Should().HaveCount(32);
        RecoveryCodeFormat.EntropyBits.Should().BeGreaterThanOrEqualTo(128);
        (RecoveryCodeFormat.Length * Math.Log2(RecoveryCodeFormat.Alphabet.Length)).Should().BeGreaterThanOrEqualTo(128);
    }

    [Fact]
    public void GenerateSet_ShouldDrawEveryAlphabetCharacter()
    {
        // 50 sets are 14,000 characters; missing any of the 32 by chance is vanishingly unlikely,
        // and a biased or truncated source would show up here.
        var seen = Enumerable.Range(0, 50)
            .SelectMany(_ => RecoveryCodeFormat.GenerateSet())
            .SelectMany(code => code)
            .ToHashSet();

        seen.Should().BeEquivalentTo(RecoveryCodeFormat.Alphabet.ToHashSet());
    }

    [Fact]
    public void Display_ShouldGroupInFours_AndRoundTripThroughNormalize()
    {
        var code = RecoveryCodeFormat.GenerateSet()[0];

        var shown = RecoveryCodeFormat.Display(code);

        shown.Split('-').Should().HaveCount(7).And.AllSatisfy(group => group.Should().HaveLength(4));
        RecoveryCodeFormat.TryNormalize(shown, out var normalized).Should().BeTrue();
        normalized.Should().Be(code);
    }

    [Theory]
    [InlineData("abcd-efgh-jkmn-pqrs-tvwx-yz01-2345", "ABCDEFGHJKMNPQRSTVWXYZ012345")]
    [InlineData(" ABCD EFGH JKMN PQRS TVWX YZ01 2345 ", "ABCDEFGHJKMNPQRSTVWXYZ012345")]
    [InlineData("OOOO-IIII-LLLL-oooo-iiii-llll-0000", "0000111111110000111111110000")]
    public void TryNormalize_ShouldIgnoreCaseSeparators_AndReadLookalikesAsDigits(string input, string expected)
    {
        RecoveryCodeFormat.TryNormalize(input, out var normalized).Should().BeTrue();

        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456")]
    [InlineData("ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-234")]
    [InlineData("ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-23456")]
    [InlineData("UUUU-EFGH-JKMN-PQRS-TVWX-YZ01-2345")]
    [InlineData("ABCD_EFGH_JKMN_PQRS_TVWX_YZ01_2345")]
    public void TryNormalize_ShouldRejectInputThatCannotBeACode(string? input)
    {
        RecoveryCodeFormat.TryNormalize(input, out var normalized).Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [Fact]
    public void TryNormalize_ShouldRejectOverlongInputWithoutReadingIt()
    {
        var input = new string('-', 60) + RecoveryCodeFormat.GenerateSet()[0];

        RecoveryCodeFormat.TryNormalize(input, out _).Should().BeFalse();
    }
}
