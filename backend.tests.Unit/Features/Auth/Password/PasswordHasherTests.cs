using backend.main.features.auth.password;

using backend.tests.Unit.Support;

using FluentAssertions;

using Microsoft.Extensions.Options;

namespace backend.tests.Unit.Features.Auth.Password;

public class PasswordHasherTests
{
    private const string Passphrase = "correct horse battery staple";

    [Fact]
    public void Hash_ShouldProduceAPhcArgon2idString_WithTheConfiguredParameters()
    {
        var hash = TestPasswordHashers.Fast().Hash(Passphrase);

        hash.Should().StartWith("$argon2id$v=19$m=1024,t=1,p=1$");
        hash.Split('$').Should().HaveCount(6);
    }

    [Fact]
    public void Hash_ShouldSaltEveryHash()
    {
        var hasher = TestPasswordHashers.Fast();

        hasher.Hash(Passphrase).Should().NotBe(hasher.Hash(Passphrase));
    }

    [Fact]
    public void Verify_ShouldSucceed_OnlyForTheHashedPassword()
    {
        var hasher = TestPasswordHashers.Fast();
        var hash = hasher.Hash(Passphrase);

        hasher.Verify(Passphrase, hash).Should().Be(PasswordVerificationResult.Success);
        hasher.Verify(Passphrase + " ", hash).Should().Be(PasswordVerificationResult.Failed);
    }

    [Theory]
    [InlineData("Ünïcödé pässphrase wïth spaces")]
    [InlineData("emoji 🔐🦄 and CJK 密码短语")]
    [InlineData("   leading and trailing spaces   ")]
    public void Verify_ShouldRoundTripUnicodeAndWhitespace(string password)
    {
        var hasher = TestPasswordHashers.Fast();

        hasher.Verify(password, hasher.Hash(password)).Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Verify_ShouldTreatCanonicallyEquivalentInputAsTheSamePassword()
    {
        var hasher = TestPasswordHashers.Fast();
        const string composed = "café au lait every morning";
        const string decomposed = "café au lait every morning";

        hasher.Verify(decomposed, hasher.Hash(composed)).Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Verify_ShouldUseEveryCharacter_OfAPassphraseLongerThanBcryptsLimit()
    {
        var hasher = TestPasswordHashers.Fast();
        var prefix = new string('a', 100);

        hasher.Verify(prefix + "x", hasher.Hash(prefix + "y")).Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void Verify_ShouldAcceptLegacyBcryptHashes_AndAskForAnUpgrade()
    {
        var hasher = TestPasswordHashers.Fast();
        var legacy = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 4);

        hasher.Verify("Password123!", legacy).Should().Be(PasswordVerificationResult.SuccessRehashNeeded);
        hasher.Verify("password123!", legacy).Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void Verify_ShouldAskForAnUpgrade_WhenArgon2ParametersChanged()
    {
        var stale = TestPasswordHashers.Fast().Hash(Passphrase);
        var options = TestPasswordHashers.FastOptions();
        options.Hashing.Argon2.Iterations = 2;

        new PasswordHasher(Options.Create(options)).Verify(Passphrase, stale)
            .Should().Be(PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Fact]
    public void Verify_ShouldAskForAnUpgrade_WhenTheAlgorithmChanged()
    {
        var argon2 = TestPasswordHashers.Fast().Hash(Passphrase);
        var bcryptHasher = TestPasswordHashers.Fast(PasswordHashAlgorithm.Bcrypt);

        bcryptHasher.Verify(Passphrase, argon2).Should().Be(PasswordVerificationResult.SuccessRehashNeeded);
        bcryptHasher.Verify(Passphrase, bcryptHasher.Hash(Passphrase)).Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Verify_ShouldAskForAnUpgrade_WhenTheBcryptCostIsBelowTheConfiguredOne()
    {
        var options = TestPasswordHashers.FastOptions(PasswordHashAlgorithm.Bcrypt);
        options.Hashing.BcryptWorkFactor = 5;
        var legacy = BCrypt.Net.BCrypt.HashPassword(Passphrase, workFactor: 4);

        new PasswordHasher(Options.Create(options)).Verify(Passphrase, legacy)
            .Should().Be(PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plaintext")]
    [InlineData("$2a$04$tooshort")]
    [InlineData("$argon2id$v=19$m=1024,t=1,p=1$c2FsdA")]
    [InlineData("$argon2id$v=16$m=1024,t=1,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=99999999,t=1,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=1024,t=1,p=1$c2FsdHNhbHRzYWx0$not*base64")]
    [InlineData("$argon2id$v=19$m=1024,t=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2i$v=19$m=1024,t=1,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNoaGFzaA")]
    public void Verify_ShouldFail_ForMissingOrMalformedHashes(string? stored)
    {
        TestPasswordHashers.Fast().Verify(Passphrase, stored).Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void DummyHash_ShouldUseTheCurrentFormat_AndBeStable()
    {
        var hasher = TestPasswordHashers.Fast();

        hasher.DummyHash.Should().StartWith("$argon2id$v=19$m=1024,t=1,p=1$");
        hasher.DummyHash.Should().Be(hasher.DummyHash);
        hasher.Verify(Passphrase, hasher.DummyHash).Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void Length_ShouldCountUserPerceivedCharacters()
    {
        PasswordText.Length("🔐🔐🔐").Should().Be(3);
        PasswordText.Length("café").Should().Be(4);
        // The "fi" ligature normalizes to two letters.
        PasswordText.Length("ﬁ").Should().Be(2);
    }

    [Fact]
    public void Normalize_ShouldLeaveUnnormalizableInputAlone()
    {
        const string loneSurrogate = "abc\ud800def";

        PasswordText.Normalize(loneSurrogate).Should().Be(loneSurrogate);
    }
}
