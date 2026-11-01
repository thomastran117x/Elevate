using backend.main.features.auth.mfa.recovery;
using backend.tests.Unit.Support;

using FluentAssertions;

using Microsoft.Extensions.Options;

namespace backend.tests.Unit.Features.Auth.RecoveryCodes;

public class RecoveryCodeHasherTests
{
    private static readonly string Code = RecoveryCodeFormat.GenerateSet()[0];

    internal static RecoveryCodeHasher Create(string? pepper = null) =>
        new(
            Options.Create(new RecoveryCodeOptions { Pepper = pepper }),
            Options.Create(TestPasswordHashers.FastOptions()));

    [Fact]
    public void Hash_ShouldProduceASaltedArgon2idPhcString()
    {
        var hasher = Create();

        var first = hasher.Hash(7, Code);
        var second = hasher.Hash(7, Code);

        first.Should().StartWith("$argon2id$v=19$m=1024,t=1,p=1$");
        first.Should().NotContain(Code);
        second.Should().NotBe(first);
    }

    [Fact]
    public void Verify_ShouldSucceed_OnlyForTheCodeItsAccountAndItsPepper()
    {
        var hasher = Create();
        var hash = hasher.Hash(7, Code);
        var otherCode = RecoveryCodeFormat.GenerateSet()[0];
        var otherPepper = Create(Convert.ToBase64String(new byte[32]));

        hasher.Verify(7, Code, hash).Should().BeTrue();
        hasher.Verify(7, otherCode, hash).Should().BeFalse();
        hasher.Verify(8, Code, hash).Should().BeFalse("the hash is bound to its account");
        otherPepper.Verify(7, Code, hash).Should().BeFalse("the hash is keyed by the pepper");
    }

    [Fact]
    public void Verify_ShouldFail_WithNoStoredHash_OrAMalformedOne()
    {
        var hasher = Create();

        hasher.Verify(7, Code, null).Should().BeFalse();
        hasher.Verify(7, Code, "not-a-hash").Should().BeFalse();
    }

    [Fact]
    public void Selector_ShouldBeDeterministic_AndKeyedByAccountAndPepper()
    {
        var hasher = Create();
        var selector = hasher.Selector(7, Code);

        selector.Should().HaveLength(32).And.MatchRegex("^[0-9A-F]+$");
        hasher.Selector(7, Code).Should().Be(selector);
        hasher.Selector(8, Code).Should().NotBe(selector);
        hasher.Selector(7, RecoveryCodeFormat.GenerateSet()[0]).Should().NotBe(selector);
        Create(Convert.ToBase64String(new byte[32])).Selector(7, Code).Should().NotBe(selector);
        selector.Should().NotContain(Code);
    }
}
