using System.Security.Cryptography;

using backend.main.features.auth.mfa.totp;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth.Mfa;

/// <summary>
/// TOTP secret encryption under a key ring: the v2 format, the legacy v1 format, rotation,
/// rollback, and failing closed on anything no configured key can open.
/// </summary>
public class TotpSecretProtectorTests
{
    private static readonly string KeyA = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private static readonly string KeyB = Convert.ToBase64String(Enumerable.Range(101, 32).Select(i => (byte)i).ToArray());
    private static readonly string KeyC = Convert.ToBase64String(Enumerable.Range(201, 32).Select(i => (byte)i).ToArray());
    private static readonly byte[] Secret = Enumerable.Range(0, 20).Select(i => (byte)(i * 7)).ToArray();

    [Fact]
    public void Protect_ShouldWriteTheKeyedFormat_NamingTheActiveKey()
    {
        var protector = new TotpSecretProtector(KeyA);

        var stored = protector.Protect(Secret);

        stored.Should().StartWith($"v2:{TotpSecretProtector.KeyIdFor(KeyA)}:");
        protector.IsProtectedWithActiveKey(stored).Should().BeTrue();
        protector.Unprotect(stored).Should().Equal(Secret);
    }

    [Fact]
    public void Protect_ShouldUseAFreshNonceEachTime()
    {
        var protector = new TotpSecretProtector(KeyA);

        protector.Protect(Secret).Should().NotBe(protector.Protect(Secret));
    }

    [Fact]
    public void Unprotect_ShouldReadLegacyV1Secrets_UnderTheActiveOrAPreviousKey()
    {
        var legacy = LegacyV1(KeyA, Secret);

        new TotpSecretProtector(KeyA).Unprotect(legacy).Should().Equal(Secret);
        new TotpSecretProtector(KeyB, [KeyA]).Unprotect(legacy).Should().Equal(Secret);
    }

    [Fact]
    public void Rotation_ShouldKeepReadingSecretsWrittenUnderThePreviousKey()
    {
        var beforeRotation = new TotpSecretProtector(KeyA).Protect(Secret);
        var rotated = new TotpSecretProtector(KeyB, [KeyA]);

        rotated.Unprotect(beforeRotation).Should().Equal(Secret);
        rotated.IsProtectedWithActiveKey(beforeRotation).Should().BeFalse();
        rotated.HasPreviousKeys.Should().BeTrue();
    }

    [Fact]
    public void Rollback_ShouldKeepReadingSecretsWrittenDuringTheRotation()
    {
        var duringRotation = new TotpSecretProtector(KeyB, [KeyA]).Protect(Secret);
        var rolledBack = new TotpSecretProtector(KeyA, [KeyB]);

        rolledBack.Unprotect(duringRotation).Should().Equal(Secret);
    }

    [Fact]
    public void Unprotect_ShouldFailClosed_WhenTheNamedKeyIsNotConfigured()
    {
        var stored = new TotpSecretProtector(KeyA).Protect(Secret);

        var act = () => new TotpSecretProtector(KeyB).Unprotect(stored);

        act.Should().Throw<CryptographicException>().WithMessage("*not configured*");
    }

    [Fact]
    public void Unprotect_ShouldFailClosed_WhenNoConfiguredKeyOpensALegacySecret()
    {
        var act = () => new TotpSecretProtector(KeyB, [KeyC]).Unprotect(LegacyV1(KeyA, Secret));

        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData("v3:abc")]
    [InlineData("plain-secret")]
    [InlineData("v2:no-separator")]
    [InlineData("v1:%%%not-base64%%%")]
    [InlineData("v1:AAAA")]
    public void Unprotect_ShouldRejectMalformedOrUnknownValues(string stored)
    {
        var act = () => new TotpSecretProtector(KeyA).Unprotect(stored);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Unprotect_ShouldRejectATamperedCiphertext()
    {
        var protector = new TotpSecretProtector(KeyA);
        var stored = protector.Protect(Secret);
        var payload = Convert.FromBase64String(stored[(stored.LastIndexOf(':') + 1)..]);
        payload[^1] ^= 0x01;
        var tampered = stored[..(stored.LastIndexOf(':') + 1)] + Convert.ToBase64String(payload);

        var act = () => protector.Unprotect(tampered);

        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("c2hvcnQ=")]
    public void Constructor_ShouldRejectKeysThatAreNot32BytesOfBase64(string key)
    {
        var act = () => new TotpSecretProtector(key);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_ShouldIgnoreBlankAndRepeatedPreviousKeys()
    {
        var protector = new TotpSecretProtector(KeyA, [" ", KeyA]);

        protector.HasPreviousKeys.Should().BeFalse();
        protector.ActiveKeyId.Should().Be(TotpSecretProtector.KeyIdFor(KeyA));
    }

    // The format TotpMfaEnrollmentService wrote before key ids: v1:base64(nonce‖tag‖ciphertext).
    private static string LegacyV1(string keyBase64, byte[] secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[secret.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Convert.FromBase64String(keyBase64), 16))
            aes.Encrypt(nonce, secret, ciphertext, tag);

        return "v1:" + Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
    }
}
