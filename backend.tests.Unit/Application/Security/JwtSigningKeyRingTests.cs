using System.IdentityModel.Tokens.Jwt;
using System.Text;

using backend.main.application.security;
using backend.main.features.auth.token;
using backend.main.features.profile;
using backend.main.shared.requests;
using backend.tests.Unit.Features.Auth;

using FluentAssertions;

using Microsoft.IdentityModel.Tokens;

namespace backend.tests.Unit.Application.Security;

/// <summary>
/// Access-token key rotation with overlap and rollback, as described in
/// docs/operations/key-rotation.md.
/// </summary>
public class JwtSigningKeyRingTests
{
    private const string OldSecret = "rotation-old-access-secret-0123456789abcdef";
    private const string NewSecret = "rotation-new-access-secret-0123456789abcdef";

    [Fact]
    public void AccessTokens_ShouldNameTheActiveKeyInTheirHeader()
    {
        var token = Issue(new JwtSigningKeyRing(NewSecret, [OldSecret]));

        new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid
            .Should().Be(JwtSigningKeyRing.KeyIdFor(NewSecret));
    }

    [Fact]
    public void KeyIds_ShouldBeStableAndDistinctPerSecret()
    {
        JwtSigningKeyRing.KeyIdFor(OldSecret).Should().Be(JwtSigningKeyRing.KeyIdFor(OldSecret));
        JwtSigningKeyRing.KeyIdFor(OldSecret).Should().NotBe(JwtSigningKeyRing.KeyIdFor(NewSecret));
        JwtSigningKeyRing.KeyIdFor(OldSecret).Should().NotContain(OldSecret);
    }

    [Fact]
    public void Rotation_ShouldKeepAcceptingTokensSignedByThePreviousKey()
    {
        var beforeRotation = Issue(new JwtSigningKeyRing(OldSecret));

        Validates(beforeRotation, new JwtSigningKeyRing(NewSecret, [OldSecret])).Should().BeTrue();
    }

    [Fact]
    public void Rollback_ShouldKeepAcceptingTokensSignedDuringTheRotation()
    {
        var duringRotation = Issue(new JwtSigningKeyRing(NewSecret, [OldSecret]));
        var beforeRotation = Issue(new JwtSigningKeyRing(OldSecret));
        var rolledBack = new JwtSigningKeyRing(OldSecret, [NewSecret]);

        Validates(duringRotation, rolledBack).Should().BeTrue();
        Validates(beforeRotation, rolledBack).Should().BeTrue();
    }

    [Fact]
    public void RetiringThePreviousKey_ShouldRejectTheTokensItSigned()
    {
        var beforeRotation = Issue(new JwtSigningKeyRing(OldSecret));

        Validates(beforeRotation, new JwtSigningKeyRing(NewSecret)).Should().BeFalse();
    }

    [Fact]
    public void TokensWithoutAKeyId_ShouldStillValidateAgainstAnyKeyInTheRing()
    {
        // Tokens issued before kid support carry no kid; the handler tries every key for them.
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "EventXperience",
            Audience = "EventXperienceConsumers",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(OldSecret)),
                SecurityAlgorithms.HmacSha256Signature),
        };
        var handler = new JwtSecurityTokenHandler();
        var legacy = handler.WriteToken(handler.CreateToken(descriptor));

        handler.ReadJwtToken(legacy).Header.Kid.Should().BeNull();
        Validates(legacy, new JwtSigningKeyRing(NewSecret, [OldSecret])).Should().BeTrue();
    }

    [Fact]
    public void Ring_ShouldSignWithTheActiveKey_AndIgnoreBlankOrRepeatedPreviousKeys()
    {
        var ring = new JwtSigningKeyRing(NewSecret, [OldSecret, " ", NewSecret, OldSecret]);

        ring.SigningKey.KeyId.Should().Be(JwtSigningKeyRing.KeyIdFor(NewSecret));
        ring.ValidationKeys.Select(key => key.KeyId).Should().Equal(
            JwtSigningKeyRing.KeyIdFor(NewSecret),
            JwtSigningKeyRing.KeyIdFor(OldSecret));
    }

    [Fact]
    public void Ring_ShouldRequireAnActiveSecret()
    {
        var act = () => new JwtSigningKeyRing(" ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task RotatingTheAccessKey_ShouldNotInvalidateRefreshSessionsOrVerificationProofs()
    {
        var cache = new InMemoryCacheService();
        var before = new TokenService(cache, signingKeys: new JwtSigningKeyRing(OldSecret));
        var after = new TokenService(cache, signingKeys: new JwtSigningKeyRing(NewSecret));
        var requestInfo = new ClientRequestInfo
        {
            IpAddress = "127.0.0.1",
            ClientName = "UnitTest",
            DeviceType = "Desktop",
            IsBrowserClient = true
        };

        var refresh = await before.GenerateRefreshToken(8, 1, requestInfo, SessionTransport.BrowserCookie);
        var emailChange = await before.GenerateEmailChangeArtifactsAsync(8, 1, "rotated@example.com");

        var refreshed = await after.ValidateRefreshToken(
            refresh.Value, refresh.SessionBindingToken, refresh.Transport, requestInfo);
        var pending = await after.ConsumeEmailChangeTokenAsync(emailChange.LinkToken);

        refreshed.SessionId.Should().Be(refresh.SessionId);
        pending.NewEmail.Should().Be("rotated@example.com");
    }

    private static string Issue(JwtSigningKeyRing ring) =>
        new TokenService(new InMemoryCacheService(), signingKeys: ring)
            .GenerateAccessToken(new User { Id = 5, Email = "rotation@example.com", Usertype = "participant", AuthVersion = 1 }, "session-1")
            .Value;

    private static bool Validates(string token, JwtSigningKeyRing ring)
    {
        try
        {
            new JwtSecurityTokenHandler().ValidateToken(
                token, JwtConfiguration.CreateTokenValidationParameters(ring), out _);
            return true;
        }
        catch (SecurityTokenException)
        {
            return false;
        }
    }
}
