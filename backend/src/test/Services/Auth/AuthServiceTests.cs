using backend.main.dtos;
using backend.main.configurations.security;
using backend.main.dtos.general;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.publishers.interfaces;
using backend.main.repositories.interfaces;
using backend.main.services.implementation;
using backend.main.services.interfaces;
using backend.test.TestSupport;
using FluentAssertions;
using Moq;
using Xunit;

namespace backend.test;

public class AuthServiceTests
{
    [Fact]
    public async Task SignUpAsync_NormalizesRoleBeforeGeneratingVerificationArtifacts()
    {
        var userRepository = new Mock<IUserRepository>();
        var tokenService = new Mock<ITokenService>();
        var publisher = new Mock<IPublisher>();
        backend.main.models.core.User? capturedUser = null;
        var expectedChallenge = new VerificationOtpChallenge
        {
            Code = "123456",
            Challenge = "challenge-id",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
        };

        userRepository.Setup(repository => repository.EmailExistsAsync("organizer@example.com"))
            .ReturnsAsync(false);
        tokenService.Setup(service => service.GenerateVerificationArtifactsAsync(
                It.IsAny<backend.main.models.core.User>(),
                VerificationPurpose.SignUp
            ))
            .Callback<backend.main.models.core.User, VerificationPurpose>((user, _) => capturedUser = user)
            .ReturnsAsync(new VerificationArtifacts
            {
                LinkToken = "link-token",
                OtpChallenge = expectedChallenge,
                Purpose = VerificationPurpose.SignUp,
            });
        publisher.Setup(client => client.PublishAsync("eventxperience-email", It.IsAny<EmailMessage>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(userRepository, publisher, tokenService: tokenService);

        var challenge = await service.SignUpAsync("organizer@example.com", "Password123!", "organizer");

        challenge.Should().BeEquivalentTo(expectedChallenge);
        capturedUser.Should().NotBeNull();
        capturedUser!.Usertype.Should().Be(AuthRoles.Organizer);
    }

    [Fact]
    public async Task LoginAsync_ReturnsUnauthorized_WhenPasswordIsInvalid()
    {
        var userRepository = new Mock<IUserRepository>();
        var publisher = new Mock<IPublisher>(MockBehavior.Strict);

        userRepository.Setup(repository => repository.GetUserByEmailAsync("user@example.com"))
            .ReturnsAsync(new User
            {
                Id = 17,
                Email = "user@example.com",
                Password = BCrypt.Net.BCrypt.HashPassword("correct-password"),
                Usertype = AuthRoles.Participant,
            });

        var service = CreateService(userRepository, publisher);

        var action = async () => await service.LoginAsync(
            "user@example.com",
            "wrong-password",
            SessionTransport.BrowserCookie
        );

        await action.Should().ThrowAsync<backend.main.exceptions.http.UnauthorizedException>();
    }

    [Fact]
    public async Task ForgotPasswordAsync_ReturnsPlaceholderChallenge_WhenEmailDoesNotExist()
    {
        var userRepository = new Mock<IUserRepository>();
        var publisher = new Mock<IPublisher>(MockBehavior.Strict);

        userRepository.Setup(repository => repository.EmailExistsAsync("missing@example.com"))
            .ReturnsAsync(false);

        var service = CreateService(userRepository, publisher);

        var challenge = await service.ForgotPasswordAsync("missing@example.com");

        challenge.Challenge.Should().NotBeNullOrWhiteSpace();
        challenge.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(25));
        publisher.Verify(
            client => client.PublishAsync(It.IsAny<string>(), It.IsAny<EmailMessage>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ForgotPasswordAsync_ReturnsRealChallenge_WhenEmailExists()
    {
        var userRepository = new Mock<IUserRepository>();
        var tokenService = new Mock<ITokenService>();
        var publisher = new Mock<IPublisher>();
        var expectedChallenge = new VerificationOtpChallenge
        {
            Code = "123456",
            Challenge = "challenge-id",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
        };

        userRepository.Setup(repository => repository.EmailExistsAsync("user@example.com"))
            .ReturnsAsync(true);
        tokenService.Setup(service => service.GenerateVerificationArtifactsAsync(
                It.IsAny<backend.main.models.core.User>(),
                VerificationPurpose.ResetPassword
            ))
            .ReturnsAsync(new VerificationArtifacts
            {
                LinkToken = "link-token",
                OtpChallenge = expectedChallenge,
                Purpose = VerificationPurpose.ResetPassword,
            });
        publisher.Setup(client => client.PublishAsync("eventxperience-email", It.IsAny<EmailMessage>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(userRepository, publisher, tokenService);

        var challenge = await service.ForgotPasswordAsync("user@example.com");

        challenge.Should().BeEquivalentTo(expectedChallenge);
    }

    [Fact]
    public async Task GoogleAsync_FirstTimeUserRequiresRoleSelection()
    {
        var userRepository = new Mock<IUserRepository>();
        var oauthService = new Mock<IOAuthService>();
        var publisher = new Mock<IPublisher>(MockBehavior.Strict);
        var cacheService = new Mock<ICacheService>();
        var oauthUser = new OAuthUser("google-1", "oauth@example.com", "OAuth User", "google");

        oauthService.Setup(service => service.VerifyGoogleTokenAsync("google-token", "expected-nonce"))
            .ReturnsAsync(oauthUser);
        userRepository.Setup(repository => repository.GetUserByGoogleIdAsync(oauthUser.Id))
            .ReturnsAsync((User?)null);
        userRepository.Setup(repository => repository.GetUserByEmailAsync(oauthUser.Email))
            .ReturnsAsync((User?)null);
        cacheService.Setup(cache => cache.SetValueAsync(
                It.Is<string>(key => key.StartsWith("oauth:pending:", StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<TimeSpan?>()
            ))
            .ReturnsAsync(true);

        var service = CreateService(
            userRepository,
            publisher,
            oauthService: oauthService,
            cacheService: cacheService
        );

        var result = await service.GoogleAsync(
            "google-token",
            SessionTransport.BrowserCookie,
            "expected-nonce"
        );

        result.RequiresRoleSelection.Should().BeTrue();
        result.PendingSignup.Should().NotBeNull();
        result.PendingSignup!.Email.Should().Be(oauthUser.Email);
        result.PendingSignup.Provider.Should().Be("google");
        userRepository.Verify(repository => repository.CreateUserAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task MicrosoftAsync_RequiresTrustedDeviceBeforeIssuingTokens()
    {
        var userRepository = new Mock<IUserRepository>();
        var oauthService = new Mock<IOAuthService>();
        var tokenService = new Mock<ITokenService>();
        var deviceService = new Mock<IDeviceService>();
        var publisher = new Mock<IPublisher>(MockBehavior.Strict);
        var oauthUser = new OAuthUser("ms-1", "oauth@example.com", "OAuth User", "microsoft");

        oauthService.Setup(service => service.VerifyMicrosoftTokenAsync("ms-token"))
            .ReturnsAsync(oauthUser);
        userRepository.Setup(repository => repository.GetUserByMicrosoftIdAsync(oauthUser.Id))
            .ReturnsAsync(new User
            {
                Id = 21,
                Email = oauthUser.Email,
                Usertype = AuthRoles.Organizer,
                MicrosoftID = oauthUser.Id,
            });
        userRepository.Setup(repository => repository.GetUserByEmailAsync(oauthUser.Email))
            .ReturnsAsync(new User
            {
                Id = 21,
                Email = oauthUser.Email,
                Usertype = AuthRoles.Organizer,
                MicrosoftID = oauthUser.Id,
            });
        tokenService.Setup(service => service.GenerateAccessToken(It.IsAny<User>()))
            .Returns("access-token");
        tokenService.Setup(service => service.GenerateRefreshToken(
                It.IsAny<int>(),
                It.IsAny<ClientRequestInfo>(),
                It.IsAny<SessionTransport>(),
                It.IsAny<string?>(),
                It.IsAny<bool?>()
            ))
            .ReturnsAsync(new RefreshTokenIssue(
                "refresh-token",
                "binding-token",
                TimeSpan.FromDays(1),
                SessionTransport.BrowserCookie
            ));
        deviceService.Setup(service => service.EnsureDeviceKnownAsync(
                21,
                oauthUser.Email,
                It.IsAny<ClientRequestInfo>()
            ))
            .Returns(Task.CompletedTask);

        var service = CreateService(
            userRepository,
            publisher,
            tokenService: tokenService,
            oauthService: oauthService,
            deviceService: deviceService
        );

        var result = await service.MicrosoftAsync("ms-token", SessionTransport.BrowserCookie);

        result.RequiresRoleSelection.Should().BeFalse();
        result.UserToken.Should().NotBeNull();
        result.UserToken!.user.Id.Should().Be(21);
        deviceService.Verify(service => service.EnsureDeviceKnownAsync(
            21,
            oauthUser.Email,
            It.IsAny<ClientRequestInfo>()
        ));
    }

    [Fact]
    public async Task CompleteOAuthSignupAsync_RejectsTransportMismatch()
    {
        var userRepository = new Mock<IUserRepository>();
        var publisher = new Mock<IPublisher>(MockBehavior.Strict);
        var cacheService = new Mock<ICacheService>();
        var signupToken = "pending-signup-token";
        var pendingState =
            "{\"ProviderUserId\":\"google-1\",\"Email\":\"oauth@example.com\",\"Name\":\"OAuth User\",\"Provider\":\"google\",\"Transport\":1}";

        cacheService.Setup(cache => cache.GetValueAsync($"oauth:pending:{signupToken}"))
            .ReturnsAsync(pendingState);

        var service = CreateService(
            userRepository,
            publisher,
            cacheService: cacheService
        );

        var action = async () => await service.CompleteOAuthSignupAsync(
            signupToken,
            "organizer",
            SessionTransport.BrowserCookie
        );

        await action.Should().ThrowAsync<backend.main.exceptions.http.UnauthorizedException>();
    }

    [Fact]
    public async Task CompleteOAuthSignupAsync_CreatesUserWithChosenRole()
    {
        var userRepository = new Mock<IUserRepository>();
        var tokenService = new Mock<ITokenService>();
        var deviceService = new Mock<IDeviceService>();
        var publisher = new Mock<IPublisher>(MockBehavior.Strict);
        var cacheService = new Mock<ICacheService>();
        User? createdUser = null;
        var signupToken = "pending-signup-token";
        var pendingState =
            "{\"ProviderUserId\":\"google-1\",\"Email\":\"oauth@example.com\",\"Name\":\"OAuth User\",\"Provider\":\"google\",\"Transport\":0}";

        cacheService.Setup(cache => cache.GetValueAsync($"oauth:pending:{signupToken}"))
            .ReturnsAsync(pendingState);
        cacheService.Setup(cache => cache.DeleteKeyAsync($"oauth:pending:{signupToken}"))
            .ReturnsAsync(true);
        userRepository.Setup(repository => repository.GetUserByGoogleIdAsync("google-1"))
            .ReturnsAsync((User?)null);
        userRepository.Setup(repository => repository.GetUserByEmailAsync("oauth@example.com"))
            .ReturnsAsync((User?)null);
        userRepository.Setup(repository => repository.CreateUserAsync(It.IsAny<User>()))
            .Callback<User>(user => createdUser = user)
            .ReturnsAsync((User user) =>
            {
                user.Id = 42;
                return user;
            });
        tokenService.Setup(service => service.GenerateAccessToken(It.IsAny<User>()))
            .Returns("access-token");
        tokenService.Setup(service => service.GenerateRefreshToken(
                It.IsAny<int>(),
                It.IsAny<ClientRequestInfo>(),
                It.IsAny<SessionTransport>(),
                It.IsAny<string?>(),
                It.IsAny<bool?>()
            ))
            .ReturnsAsync(new RefreshTokenIssue(
                "refresh-token",
                "binding-token",
                TimeSpan.FromDays(1),
                SessionTransport.BrowserCookie
            ));
        deviceService.Setup(service => service.EnsureDeviceKnownAsync(
                42,
                "oauth@example.com",
                It.IsAny<ClientRequestInfo>()
            ))
            .Returns(Task.CompletedTask);

        var service = CreateService(
            userRepository,
            publisher,
            tokenService: tokenService,
            deviceService: deviceService,
            cacheService: cacheService
        );

        var result = await service.CompleteOAuthSignupAsync(
            signupToken,
            "organizer",
            SessionTransport.BrowserCookie
        );

        createdUser.Should().NotBeNull();
        createdUser!.Usertype.Should().Be(AuthRoles.Organizer);
        createdUser.GoogleID.Should().Be("google-1");
        result.user.Id.Should().Be(42);
    }

    private static AuthService CreateService(
        Mock<IUserRepository> userRepository,
        Mock<IPublisher> publisher,
        Mock<ITokenService>? tokenService = null,
        Mock<IOAuthService>? oauthService = null,
        Mock<IDeviceService>? deviceService = null,
        Mock<ICacheService>? cacheService = null
    )
    {
        return new AuthService(
            userRepository.Object,
            (oauthService ?? new Mock<IOAuthService>()).Object,
            (tokenService ?? new Mock<ITokenService>()).Object,
            (cacheService ?? InMemoryCacheMock.Create()).Object,
            publisher.Object,
            (deviceService ?? new Mock<IDeviceService>()).Object,
            new ClientRequestInfo()
        );
    }
}
