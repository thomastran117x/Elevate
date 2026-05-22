using backend.main.features.auth;
using backend.main.features.auth.contracts;
using backend.main.features.auth.device;
using backend.main.features.auth.oauth;
using backend.main.features.auth.token;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;

using backend.tests.Unit.Support;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class AuthServiceTests
{
    [Fact]
    public async Task LoginAsync_ShouldRejectUnknownUser()
    {
        var userRepository = new Mock<IAuthUserRepository>();
        userRepository.Setup(repository => repository.GetAuthByEmailAsync("missing@example.com"))
            .ReturnsAsync((UserAuthRecord?)null);

        var service = CreateService(userRepository: userRepository);

        var act = () => service.LoginAsync(
            "missing@example.com",
            "Password123!",
            SessionTransport.BrowserCookie);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid email or password");
    }

    [Fact]
    public async Task HandleTokensAsync_ShouldRevokeSessionsForDisabledUsers()
    {
        var userRepository = new Mock<IAuthUserRepository>();
        userRepository.Setup(repository => repository.GetUserAsync(44))
            .ReturnsAsync(new TestUserBuilder().WithId(44).Disabled().Build());

        var tokenService = new Mock<ITokenService>();
        tokenService.Setup(service => service.ValidateRefreshToken(
                "refresh-token",
                "binding-token",
                SessionTransport.BrowserCookie,
                It.IsAny<backend.main.shared.requests.ClientRequestInfo>()))
            .ReturnsAsync(new RefreshTokenValidationResult
            {
                SessionId = "session-44",
                UserId = 44,
                Transport = SessionTransport.BrowserCookie
            });

        var service = CreateService(userRepository: userRepository, tokenService: tokenService);

        var act = () => service.HandleTokensAsync(
            "refresh-token",
            "binding-token",
            SessionTransport.BrowserCookie);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("This account is disabled.");

        tokenService.Verify(s => s.RevokeAllRefreshSessionsAsync(44), Times.Once);
    }

    [Fact]
    public async Task SignUpAsync_ShouldNormalizeRoleBeforeCreatingVerificationArtifacts()
    {
        var userRepository = new Mock<IAuthUserRepository>();
        userRepository.Setup(repository => repository.EmailExistsAsync("new@example.com"))
            .ReturnsAsync(false);

        User? capturedUser = null;
        var tokenService = new Mock<ITokenService>();
        tokenService.Setup(service => service.GenerateVerificationArtifactsAsync(
                It.IsAny<backend.main.features.profile.User>(),
                VerificationPurpose.SignUp))
            .Callback<backend.main.features.profile.User, VerificationPurpose>((user, _) => capturedUser = user)
            .ReturnsAsync(new VerificationArtifacts
            {
                LinkToken = "verify-link",
                Purpose = VerificationPurpose.SignUp,
                OtpChallenge = new VerificationOtpChallenge
                {
                    Code = "123456",
                    Challenge = "challenge",
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
                }
            });

        var publisher = new Mock<IPublisher>();
        var service = CreateService(
            userRepository: userRepository,
            tokenService: tokenService,
            publisher: publisher);

        var result = await service.SignUpAsync("new@example.com", "Password123!", "organizer");

        result.Code.Should().Be("123456");
        capturedUser.Should().NotBeNull();
        capturedUser!.Usertype.Should().Be("Organizer");
        publisher.Verify(p => p.PublishAsync(
            "eventxperience-email",
            It.Is<EmailMessage>(message => message.Type == EmailMessageType.VerifyEmail
                && message.Email == "new@example.com"
                && message.Token == "verify-link"
                && message.Code == "123456")),
            Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_ShouldReturnPlaceholderWithoutPublishingForUnknownUsers()
    {
        var userRepository = new Mock<IAuthUserRepository>();
        userRepository.Setup(repository => repository.GetAuthByEmailAsync("unknown@example.com"))
            .ReturnsAsync((UserAuthRecord?)null);

        var publisher = new Mock<IPublisher>();
        var service = CreateService(userRepository: userRepository, publisher: publisher);

        var challenge = await service.ForgotPasswordAsync("unknown@example.com");

        challenge.Challenge.Should().NotBeNullOrWhiteSpace();
        challenge.Code.Should().HaveLength(6);
        challenge.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        publisher.Verify(
            p => p.PublishAsync(It.IsAny<string>(), It.IsAny<EmailMessage>()),
            Times.Never);
    }

    [Fact]
    public async Task GoogleAsync_ShouldAttachProviderIdForExistingEmailUser()
    {
        var userRepository = new Mock<IAuthUserRepository>();
        userRepository.Setup(repository => repository.GetOAuthByGoogleIdAsync("google-1"))
            .ReturnsAsync((UserOAuthRecord?)null);
        userRepository.Setup(repository => repository.GetOAuthByEmailAsync("existing@example.com"))
            .ReturnsAsync(new UserOAuthRecord
            {
                Id = 9,
                Email = "existing@example.com",
                Usertype = "Participant",
                GoogleID = null,
                MicrosoftID = null,
                AuthVersion = 1
            });
        userRepository.Setup(repository => repository.UpdateProviderIdsAsync(9, "google-1", null))
            .ReturnsAsync(new UserOAuthRecord
            {
                Id = 9,
                Email = "existing@example.com",
                Usertype = "Participant",
                GoogleID = "google-1",
                MicrosoftID = null,
                AuthVersion = 1
            });

        var oauthService = new Mock<IOAuthService>();
        oauthService.Setup(service => service.VerifyGoogleTokenAsync("google-token", null))
            .ReturnsAsync(new OAuthUser("google-1", "existing@example.com", "Existing User", "google"));

        var tokenService = new Mock<ITokenService>();
        tokenService.Setup(service => service.GenerateAccessToken(It.IsAny<backend.main.features.profile.User>()))
            .Returns(new AccessTokenIssue("access-token", DateTime.UtcNow.AddMinutes(15)));
        tokenService.Setup(service => service.GenerateRefreshToken(
                9,
                It.IsAny<backend.main.shared.requests.ClientRequestInfo>(),
                SessionTransport.BrowserCookie,
                null,
                null))
            .ReturnsAsync(new RefreshTokenIssue(
                "refresh-token",
                "binding-token",
                TimeSpan.FromDays(1),
                SessionTransport.BrowserCookie));

        var deviceService = new Mock<IDeviceService>();
        var service = CreateService(
            userRepository: userRepository,
            oauthService: oauthService,
            tokenService: tokenService,
            deviceService: deviceService);

        var result = await service.GoogleAsync("google-token", SessionTransport.BrowserCookie);

        result.RequiresRoleSelection.Should().BeFalse();
        result.UserToken.Should().NotBeNull();
        result.UserToken!.user.GoogleID.Should().Be("google-1");
        userRepository.Verify(repository => repository.UpdateProviderIdsAsync(9, "google-1", null), Times.Once);
        deviceService.Verify(s => s.EnsureDeviceKnownAsync(9, "existing@example.com", It.IsAny<backend.main.shared.requests.ClientRequestInfo>()), Times.Once);
    }

    [Fact]
    public async Task GoogleAsync_ShouldRejectConflictingProviderAndEmailUsers()
    {
        var userRepository = new Mock<IAuthUserRepository>();
        userRepository.Setup(repository => repository.GetOAuthByGoogleIdAsync("google-1"))
            .ReturnsAsync(new UserOAuthRecord
            {
                Id = 1,
                Email = "provider@example.com",
                Usertype = "Participant",
                GoogleID = "google-1",
                AuthVersion = 1
            });
        userRepository.Setup(repository => repository.GetOAuthByEmailAsync("existing@example.com"))
            .ReturnsAsync(new UserOAuthRecord
            {
                Id = 2,
                Email = "existing@example.com",
                Usertype = "Participant",
                AuthVersion = 1
            });

        var oauthService = new Mock<IOAuthService>();
        oauthService.Setup(service => service.VerifyGoogleTokenAsync("google-token", null))
            .ReturnsAsync(new OAuthUser("google-1", "existing@example.com", "Existing User", "google"));

        var service = CreateService(userRepository: userRepository, oauthService: oauthService);

        var act = () => service.GoogleAsync("google-token", SessionTransport.BrowserCookie);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("This Google account is already linked to another user.");
    }

    private static AuthService CreateService(
        Mock<IAuthUserRepository>? userRepository = null,
        Mock<IOAuthService>? oauthService = null,
        Mock<ITokenService>? tokenService = null,
        Mock<IPublisher>? publisher = null,
        Mock<IDeviceService>? deviceService = null)
    {
        userRepository ??= new Mock<IAuthUserRepository>();
        oauthService ??= new Mock<IOAuthService>();
        tokenService ??= new Mock<ITokenService>();
        publisher ??= new Mock<IPublisher>();
        deviceService ??= new Mock<IDeviceService>();

        return new AuthService(
            userRepository.Object,
            oauthService.Object,
            tokenService.Object,
            Mock.Of<backend.main.features.cache.ICacheService>(),
            publisher.Object,
            deviceService.Object,
            TestRequestInfoFactory.Browser());
    }
}
