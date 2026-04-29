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
            Usertype = "organizer"
        };

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(service.GenerateAccessToken(user));
        var roleClaim = jwt.Claims.First(claim =>
            claim.Type == ClaimTypes.Role || claim.Type == "role"
        ).Value;

        roleClaim.Should().Be(AuthRoles.Organizer);
    }

    [Fact]
    public async Task GenerateAndValidateRefreshToken_BrowserTransport_RequiresBindingToken()
    {
        var cache = CreateCacheMock();
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
        var cache = CreateCacheMock();
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
        var cache = CreateCacheMock();
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
    public async Task GenerateRefreshToken_RotationIssuesNewRefreshAndBindingTokens()
    {
        var cache = CreateCacheMock();
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
    public async Task GenerateVerificationArtifactsAsync_SignUpChallenge_IsOpaqueAndDoesNotExposePasswordData()
    {
        var cache = CreateCacheMock();
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
        var cache = CreateCacheMock();
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

    private static Mock<ICacheService> CreateCacheMock()
    {
        var values = new Dictionary<string, string>();
        var counters = new Dictionary<string, long>();
        var sets = new Dictionary<string, HashSet<string>>();
        var mock = new Mock<ICacheService>();

        mock.Setup(cache => cache.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => values.TryGetValue(key, out var value) ? value : null);

        mock.Setup(cache => cache.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((string key, string value, TimeSpan? _) =>
            {
                values[key] = value;
                return true;
            });

        mock.Setup(cache => cache.DeleteKeyAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) =>
            {
                var removedValue = values.Remove(key);
                var removedCounter = counters.Remove(key);
                var removedSet = sets.Remove(key);
                return removedValue || removedCounter || removedSet;
            });

        mock.Setup(cache => cache.IncrementAsync(It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync((string key, long amount) =>
            {
                counters.TryGetValue(key, out var current);
                current += amount;
                counters[key] = current;
                return current;
            });

        mock.Setup(cache => cache.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        mock.Setup(cache => cache.SetAddAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string key, string value) =>
            {
                if (!sets.TryGetValue(key, out var members))
                {
                    members = new HashSet<string>(StringComparer.Ordinal);
                    sets[key] = members;
                }

                return members.Add(value);
            });

        mock.Setup(cache => cache.SetRemoveAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string key, string value) =>
            {
                if (!sets.TryGetValue(key, out var members))
                    return false;

                var removed = members.Remove(value);
                if (members.Count == 0)
                    sets.Remove(key);

                return removed;
            });

        mock.Setup(cache => cache.SetMembersAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) =>
            {
                if (!sets.TryGetValue(key, out var members))
                    return Array.Empty<string>();

                return members.ToArray();
            });

        return mock;
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
