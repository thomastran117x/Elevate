using System.Text;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.main.configurations.security;
using backend.main.dtos.general;
using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.services.implementation;
using backend.main.services.interfaces;
using backend.test.TestSupport;
using FluentAssertions;
using Moq;
using Xunit;

namespace backend.test;

public class TokenServiceTests
{
    [Fact]
    public void GenerateAccessToken_NormalizesOrganizerRoleClaim()
    {
        var service = new TokenService(Mock.Of<ICacheService>());
        var user = new User
        {
            Id = 17,
            Email = "organizer@example.com",
            Usertype = "organizer",
            AuthVersion = 4,
        };

        var issued = service.GenerateAccessToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.Value);
        var roleClaim = jwt.Claims.First(claim =>
            claim.Type == ClaimTypes.Role || claim.Type == "role"
        ).Value;
        var authVersionClaim = jwt.Claims.First(claim => claim.Type == TokenService.AuthVersionClaimType).Value;

        roleClaim.Should().Be(AuthRoles.Organizer);
        authVersionClaim.Should().Be("4");
        issued.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(10));
    }

    [Fact]
    public async Task GenerateAndValidateRefreshToken_BrowserTransport_RequiresBindingToken()
    {
        var cache = InMemoryCacheMock.Create();
        var service = new TokenService(cache.Object);
        var requestInfo = new ClientRequestInfo
        {
            IpAddress = "10.0.0.1",
            ClientName = "Chrome",
            DeviceType = "Desktop",
        };

        var issued = await service.GenerateRefreshToken(
            42,
            requestInfo,
            SessionTransport.BrowserCookie
        );

        var validation = await service.ValidateRefreshToken(
            issued.Value,
            issued.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo
        );

        validation.UserId.Should().Be(42);
        validation.Transport.Should().Be(SessionTransport.BrowserCookie);
        validation.SessionId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ValidateRefreshToken_MissingBindingToken_RevokesSession()
    {
        var cache = InMemoryCacheMock.Create();
        var service = new TokenService(cache.Object);
        var requestInfo = new ClientRequestInfo
        {
            IpAddress = "10.0.0.1",
            ClientName = "Chrome",
            DeviceType = "Desktop",
        };

        var issued = await service.GenerateRefreshToken(
            42,
            requestInfo,
            SessionTransport.BrowserCookie
        );

        await FluentActions.Awaiting(() => service.ValidateRefreshToken(
                issued.Value,
                null,
                SessionTransport.BrowserCookie,
                requestInfo
            ))
            .Should().ThrowAsync<UnauthorizedException>();

        await FluentActions.Awaiting(() => service.ValidateRefreshToken(
                issued.Value,
                issued.SessionBindingToken,
                SessionTransport.BrowserCookie,
                requestInfo
            ))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task ValidateRefreshToken_TransportMismatch_RevokesSession()
    {
        var cache = InMemoryCacheMock.Create();
        var service = new TokenService(cache.Object);
        var requestInfo = new ClientRequestInfo
        {
            IpAddress = "10.0.0.1",
            ClientName = "Chrome",
            DeviceType = "Desktop",
        };

        var issued = await service.GenerateRefreshToken(
            42,
            requestInfo,
            SessionTransport.BrowserCookie
        );

        await FluentActions.Awaiting(() => service.ValidateRefreshToken(
                issued.Value,
                issued.SessionBindingToken,
                SessionTransport.ApiToken,
                requestInfo
            ))
            .Should().ThrowAsync<UnauthorizedException>();

        await FluentActions.Awaiting(() => service.ValidateRefreshToken(
                issued.Value,
                issued.SessionBindingToken,
                SessionTransport.BrowserCookie,
                requestInfo
            ))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task ValidateRefreshToken_InvalidBindingToken_RevokesSession()
    {
        var cache = InMemoryCacheMock.Create();
        var service = new TokenService(cache.Object);
        var requestInfo = new ClientRequestInfo
        {
            IpAddress = "10.0.0.1",
            ClientName = "Chrome",
            DeviceType = "Desktop",
        };

        var issued = await service.GenerateRefreshToken(
            42,
            requestInfo,
            SessionTransport.BrowserCookie
        );

        await FluentActions.Awaiting(() => service.ValidateRefreshToken(
                issued.Value,
                "wrong-binding-token",
                SessionTransport.BrowserCookie,
                requestInfo
            ))
            .Should().ThrowAsync<UnauthorizedException>();

        await FluentActions.Awaiting(() => service.ValidateRefreshToken(
                issued.Value,
                issued.SessionBindingToken,
                SessionTransport.BrowserCookie,
                requestInfo
            ))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task GenerateRefreshToken_RotationIssuesNewRefreshAndBindingTokens()
    {
        var cache = InMemoryCacheMock.Create();
        var service = new TokenService(cache.Object);
        var requestInfo = new ClientRequestInfo
        {
            IpAddress = "10.0.0.1",
            ClientName = "Chrome",
            DeviceType = "Desktop",
        };

        var firstIssue = await service.GenerateRefreshToken(
            42,
            requestInfo,
            SessionTransport.BrowserCookie
        );
        var validation = await service.ValidateRefreshToken(
            firstIssue.Value,
            firstIssue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo
        );
        var rotatedIssue = await service.GenerateRefreshToken(
            42,
            requestInfo,
            SessionTransport.BrowserCookie,
            validation.SessionId
        );

        rotatedIssue.Value.Should().NotBe(firstIssue.Value);
        rotatedIssue.SessionBindingToken.Should().NotBe(firstIssue.SessionBindingToken);
        rotatedIssue.Transport.Should().Be(SessionTransport.BrowserCookie);
    }

    [Fact]
    public async Task RevokeAllRefreshSessionsAsync_RemovesEverySessionForUser()
    {
        var cache = InMemoryCacheMock.Create();
        var service = new TokenService(cache.Object);
        var requestInfo = new ClientRequestInfo
        {
            IpAddress = "10.0.0.1",
            ClientName = "Chrome",
            DeviceType = "Desktop",
        };

        var first = await service.GenerateRefreshToken(42, requestInfo, SessionTransport.BrowserCookie);
        var second = await service.GenerateRefreshToken(42, requestInfo, SessionTransport.ApiToken);

        await service.RevokeAllRefreshSessionsAsync(42);

        await FluentActions.Awaiting(() => service.ValidateRefreshToken(
                first.Value,
                first.SessionBindingToken,
                SessionTransport.BrowserCookie,
                requestInfo
            ))
            .Should().ThrowAsync<UnauthorizedException>();

        await FluentActions.Awaiting(() => service.ValidateRefreshToken(
                second.Value,
                second.SessionBindingToken,
                SessionTransport.ApiToken,
                requestInfo
            ))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task GenerateVerificationArtifactsAsync_SignUpChallenge_IsOpaqueAndDoesNotExposePasswordData()
    {
        var cache = InMemoryCacheMock.Create();
        var service = new TokenService(cache.Object);
        var user = new User
        {
            Email = "user@example.com",
            Password = "$2b$12$abcdefghijklmnopqrstuvABCDEFGHijklmnopqrstuvABCD",
            Usertype = "Participant"
        };

        var artifacts = await service.GenerateVerificationArtifactsAsync(
            user,
            VerificationPurpose.SignUp
        );

        artifacts.OtpChallenge.Challenge.Should().NotBeNullOrWhiteSpace();
        artifacts.OtpChallenge.Challenge.Should().NotContain(user.Password);
        var decodedPayload = TryDecodeJwtPayload(artifacts.OtpChallenge.Challenge);

        decodedPayload.Should()
            .BeNull("client-visible verification challenges should be opaque, not self-contained JWTs");
        decodedPayload?.Keys.Should()
            .NotContain(key => key.Contains("password", StringComparison.OrdinalIgnoreCase));
        decodedPayload?.Keys.Should()
            .NotContain(key => key.Contains("hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VerifyVerificationOtpAsync_SignUpChallenge_UsesServerSideState()
    {
        var cache = InMemoryCacheMock.Create();
        var service = new TokenService(cache.Object);
        var user = new User
        {
            Email = "user@example.com",
            Password = "$2b$12$abcdefghijklmnopqrstuvABCDEFGHijklmnopqrstuvABCD",
            Usertype = "Participant"
        };

        var artifacts = await service.GenerateVerificationArtifactsAsync(
            user,
            VerificationPurpose.SignUp
        );

        var verifiedUser = await service.VerifyVerificationOtpAsync(
            artifacts.OtpChallenge.Code,
            artifacts.OtpChallenge.Challenge,
            VerificationPurpose.SignUp
        );

        verifiedUser.Email.Should().Be(user.Email);
        verifiedUser.Password.Should().Be(user.Password);
        verifiedUser.Usertype.Should().Be(user.Usertype);
    }

    private static Dictionary<string, JsonElement>? TryDecodeJwtPayload(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        try
        {
            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch
        {
            return null;
        }
    }
}
