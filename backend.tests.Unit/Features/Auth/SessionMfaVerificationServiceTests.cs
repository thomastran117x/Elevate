using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.session;
using backend.main.features.auth.mfa.totp;
using backend.main.features.auth.notifications;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class SessionMfaVerificationServiceTests
{
    private const int UserId = 77;
    private const string Email = "member@example.com";
    private const string SessionId = "session-xyz";

    [Fact]
    public async Task VerifyAsync_WithEmailCode_ShouldMarkSessionVerified()
    {
        var notifications = new Mock<IAuthNotificationService>();
        string? sentCode = null;
        notifications
            .Setup(n => n.SendEmailMfaCodeAsync(Email, It.IsAny<string>(), It.IsAny<string?>()))
            .Callback<string, string, string?>((_, code, _) => sentCode = code)
            .Returns(Task.CompletedTask);

        var (service, _) = CreateService(notifications: notifications);

        await service.StartAsync(UserId, Email, "email");
        sentCode.Should().NotBeNullOrWhiteSpace();

        (await service.IsSessionVerifiedAsync(SessionId)).Should().BeFalse();

        await service.VerifyAsync(UserId, Email, SessionId, "email", sentCode!);

        (await service.IsSessionVerifiedAsync(SessionId)).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_WithWrongEmailCode_ShouldThrow_AndNotMarkVerified()
    {
        var notifications = new Mock<IAuthNotificationService>();
        notifications
            .Setup(n => n.SendEmailMfaCodeAsync(Email, It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var (service, _) = CreateService(notifications: notifications);

        await service.StartAsync(UserId, Email, "email");

        var act = () => service.VerifyAsync(UserId, Email, SessionId, "email", "000000");

        await act.Should().ThrowAsync<UnauthorizedException>();
        (await service.IsSessionVerifiedAsync(SessionId)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WithValidTotp_ShouldMarkSessionVerified()
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "123456")).Returns(Task.CompletedTask);

        var (service, _) = CreateService(totp: totp);

        await service.VerifyAsync(UserId, Email, SessionId, "totp", "123456");

        (await service.IsSessionVerifiedAsync(SessionId)).Should().BeTrue();
        totp.Verify(t => t.VerifyPersistedCodeAsync(UserId, "123456"), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_WithInvalidTotp_ShouldPropagate_AndNotMarkVerified()
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, It.IsAny<string>()))
            .ThrowsAsync(new UnauthorizedException("Invalid or expired TOTP code."));

        var (service, _) = CreateService(totp: totp);

        var act = () => service.VerifyAsync(UserId, Email, SessionId, "totp", "999999");

        await act.Should().ThrowAsync<UnauthorizedException>();
        (await service.IsSessionVerifiedAsync(SessionId)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WithoutSessionId_ShouldThrow()
    {
        var (service, _) = CreateService();

        var act = () => service.VerifyAsync(UserId, Email, string.Empty, "totp", "123456");

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task GetOptionsAsync_ShouldAlwaysOfferEmail()
    {
        var (service, _) = CreateService();

        var options = await service.GetOptionsAsync(UserId, Email);

        options.AvailableMethods.Should().Contain("email");
        options.MaskedEmail.Should().NotBeNullOrWhiteSpace();
    }

    private static (SessionMfaVerificationService service, InMemoryCacheService cache) CreateService(
        Mock<IAuthNotificationService>? notifications = null,
        Mock<ITotpMfaEnrollmentService>? totp = null,
        Mock<IMfaEnrollmentRepository>? smsRepository = null)
    {
        notifications ??= new Mock<IAuthNotificationService>();
        totp ??= new Mock<ITotpMfaEnrollmentService>();
        smsRepository ??= new Mock<IMfaEnrollmentRepository>();

        var tokenService = new Mock<ITokenService>();
        tokenService.Setup(t => t.GetRefreshSessionTtlAsync(It.IsAny<string>()))
            .ReturnsAsync(TimeSpan.FromDays(1));

        var cache = new InMemoryCacheService();

        var service = new SessionMfaVerificationService(
            cache,
            notifications.Object,
            smsRepository.Object,
            totp.Object,
            tokenService.Object);

        return (service, cache);
    }
}
