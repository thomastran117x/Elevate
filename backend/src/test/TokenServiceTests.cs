using System.Text;
using System.Text.Json;
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
    public async Task GenerateVerificationArtifactsAsync_SignUpChallenge_IsOpaqueAndDoesNotExposePasswordData()
    {
        var cache = CreateCacheMock();
        var service = new TokenService(cache.Object);
        var user = new User
        {
            Email = "user@example.com",
            Password = "$2b$12$abcdefghijklmnopqrstuvABCDEFGHijklmnopqrstuvABCD",
            Usertype = "attendee"
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
            Usertype = "attendee"
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
                return removedValue || removedCounter;
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
