using backend.main.features.auth.password;

using FluentAssertions;

using Microsoft.Extensions.Options;

namespace backend.tests.Unit.Features.Auth.Password;

public class PasswordBlocklistTests
{
    [Fact]
    public void EmbeddedList_ShouldLoadTheBundledCommonPasswords()
    {
        var blocklist = new PasswordBlocklist(Options.Create(new PasswordPolicyOptions()));

        blocklist.Count.Should().BeGreaterThan(90_000);
        blocklist.Contains("password").Should().BeTrue();
        blocklist.Contains("qwerty").Should().BeTrue();
        blocklist.Contains("iloveyou").Should().BeTrue();
        blocklist.Contains("lanternorchardvelvetcompass").Should().BeFalse();
    }

    [Fact]
    public void EmbeddedList_ShouldSkipItsAttributionComments()
    {
        var blocklist = new PasswordBlocklist(Options.Create(new PasswordPolicyOptions()));

        blocklist.Contains(PasswordText.Skeleton("# SecLists is MIT licensed")).Should().BeFalse();
    }

    [Fact]
    public void Contains_ShouldNeverMatchAnEmptySkeleton()
    {
        new PasswordBlocklist(Options.Create(new PasswordPolicyOptions())).Contains("").Should().BeFalse();
    }

    [Fact]
    public void ConfiguredFile_ShouldBeMergedAndNormalized()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, ["# local additions", "Harbour-Striders 2026", "", "MÜNCHEN rocks"]);
            var blocklist = new PasswordBlocklist(Options.Create(new PasswordPolicyOptions
            {
                UseEmbeddedBlocklist = false,
                BlocklistPath = path,
            }));

            blocklist.Count.Should().Be(2);
            blocklist.Contains("harbourstriders2026").Should().BeTrue();
            blocklist.Contains(PasswordText.Skeleton("münchen ROCKS")).Should().BeTrue();
            blocklist.Contains("password").Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OptionsValidator_ShouldRejectAMissingBlocklistFile()
    {
        var result = new PasswordPolicyOptionsValidator().Validate(null, new PasswordPolicyOptions
        {
            BlocklistPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt"),
        });

        result.FailureMessage.Should().Contain("BlocklistPath");
    }

    [Theory]
    [InlineData(7, 128, "MinLength")]
    [InlineData(15, 129, "MaxLength")]
    [InlineData(20, 16, "MaxLength")]
    public void OptionsValidator_ShouldRejectInvalidLengthBounds(int min, int max, string expectedKey)
    {
        var result = new PasswordPolicyOptionsValidator().Validate(null, new PasswordPolicyOptions
        {
            MinLength = min,
            MaxLength = max,
        });

        result.FailureMessage.Should().Contain(expectedKey);
    }
}
