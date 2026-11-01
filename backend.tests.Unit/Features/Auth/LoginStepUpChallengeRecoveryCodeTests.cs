using backend.main.features.auth;
using backend.main.features.auth.device;
using backend.main.features.auth.events;
using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.recovery;
using backend.main.features.auth.mfa.totp;
using backend.main.features.auth.notifications;
using backend.main.features.auth.stepup;
using backend.main.features.auth.token;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;

using backend.tests.Unit.Features.Auth.Events;
using backend.tests.Unit.Support;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class LoginStepUpChallengeRecoveryCodeTests
{
    private const int UserId = 9;

    private static readonly string ValidCode = RecoveryCodeFormat.Display(RecoveryCodeFormat.GenerateSet()[0]);

    private readonly InMemoryCacheService _cache = new();
    private readonly Mock<IRecoveryCodeService> _recoveryCodes = new();
    private readonly Mock<IDeviceTrustService> _deviceTrust = new();
    private readonly Mock<IAuthUserRepository> _users = new();
    private readonly Mock<IAuthSessionService> _sessions = new();
    private readonly RecordingAuthSecurityEventEmitter _securityEvents = new();
    private readonly User _user = new TestUserBuilder().WithId(UserId).WithEmail("recovery-login@example.com").Build();
    private readonly LoginStepUpChallengeService _service;

    public LoginStepUpChallengeRecoveryCodeTests()
    {
        _recoveryCodes.Setup(r => r.HasUnusedAsync(UserId)).ReturnsAsync(true);
        _users.Setup(r => r.GetUserAsync(UserId)).ReturnsAsync(_user);
        _sessions
            .Setup(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<SessionTransport>(), It.IsAny<string?>(), It.IsAny<bool?>()))
            .ReturnsAsync(new UserToken(
                new Token("access-token", DateTime.UtcNow.AddMinutes(15), "refresh-token", "binding-token", TimeSpan.FromDays(1), SessionTransport.ApiToken),
                _user));
        _service = new LoginStepUpChallengeService(
            _cache,
            new Mock<IAuthNotificationService>().Object,
            new Mock<IMfaEnrollmentRepository>().Object,
            new Mock<ITotpMfaEnrollmentService>().Object,
            _deviceTrust.Object,
            _users.Object,
            _sessions.Object,
            new Mock<ITokenService>().Object,
            TestRequestInfoFactory.Browser(),
            _securityEvents,
            _recoveryCodes.Object);
    }

    [Fact]
    public async Task CreateChallengeAsync_ShouldOfferRecoveryCodes_BeforeEmail_OnlyWhileSomeRemain()
    {
        (await _service.CreateChallengeAsync(_user, SessionTransport.ApiToken, false, null))
            .AvailableMethods.Should().Equal("recovery_code", "email");

        _recoveryCodes.Setup(r => r.HasUnusedAsync(UserId)).ReturnsAsync(false);
        (await _service.CreateChallengeAsync(_user, SessionTransport.ApiToken, false, null))
            .AvailableMethods.Should().Equal("email");
    }

    [Fact]
    public async Task StartAsync_WithRecoveryCode_ShouldKeepTheChallenge_AndDeliverNothing()
    {
        var challenge = await _service.CreateChallengeAsync(_user, SessionTransport.ApiToken, false, null);

        var started = await _service.StartAsync(challenge.Challenge, "recovery_code");

        started.Challenge.Should().Be(challenge.Challenge);
        started.SelectedMethod.Should().Be("recovery_code");
        started.MaskedDestination.Should().Be("recovery code");
    }

    [Fact]
    public async Task VerifyRecoveryCodeAsync_ShouldSignIn_WithoutTrustingTheDevice()
    {
        _recoveryCodes.Setup(r => r.TryConsumeAsync(UserId, ValidCode)).ReturnsAsync(true);
        var challenge = await _service.CreateChallengeAsync(_user, SessionTransport.ApiToken, false, "/events/4");

        var result = await _service.VerifyRecoveryCodeAsync(challenge.Challenge, ValidCode);

        result.ReturnPath.Should().Be("/events/4");
        result.UserToken.token.AccessToken.Should().Be("access-token");
        _deviceTrust.VerifyNoOtherCalls();
        var used = _securityEvents.Of(AuthSecurityEvents.RecoveryCodeUsed).Should().ContainSingle().Subject;
        used.UserId.Should().Be(UserId);
        used.Flow.Should().Be(AuthSecurityFlow.LoginStepUp);
        _securityEvents.Of(AuthSecurityEvents.StepUpSucceeded).Should().ContainSingle()
            .Which.Method.Should().Be(AuthSecurityMethod.RecoveryCode);

        // The challenge is spent with the sign-in.
        var replay = () => _service.VerifyRecoveryCodeAsync(challenge.Challenge, ValidCode);
        await replay.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task VerifyRecoveryCodeAsync_ShouldRejectAWrongCode_AndDiscardTheChallengeAfterMaxAttempts()
    {
        var challenge = await _service.CreateChallengeAsync(_user, SessionTransport.ApiToken, false, null);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var act = () => _service.VerifyRecoveryCodeAsync(challenge.Challenge, ValidCode);
            await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("Invalid or already used recovery code.");
        }

        var afterMax = () => _service.VerifyRecoveryCodeAsync(challenge.Challenge, ValidCode);
        await afterMax.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired sign-in verification challenge.");
        _sessions.Verify(
            s => s.IssueAsync(It.IsAny<User>(), It.IsAny<SessionTransport>(), It.IsAny<string?>(), It.IsAny<bool?>()),
            Times.Never);
        _securityEvents.Of(AuthSecurityEvents.StepUpFailed)
            .Select(e => e.Reason)
            .Should().Contain(AuthSecurityReason.AttemptsExhausted);
    }

    [Fact]
    public async Task VerifyRecoveryCodeAsync_ShouldNotSpendACode_ForADisabledAccount()
    {
        var challenge = await _service.CreateChallengeAsync(_user, SessionTransport.ApiToken, false, null);
        _user.IsDisabled = true;

        var act = () => _service.VerifyRecoveryCodeAsync(challenge.Challenge, ValidCode);

        await act.Should().ThrowAsync<ForbiddenException>();
        _recoveryCodes.Verify(r => r.TryConsumeAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task VerifyRecoveryCodeAsync_ShouldNotSpendACode_ForAnExpiredOrUnknownChallenge()
    {
        var act = () => _service.VerifyRecoveryCodeAsync("no-such-challenge", ValidCode);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired sign-in verification challenge.");
        _recoveryCodes.Verify(r => r.TryConsumeAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task VerifyRecoveryCodeAsync_ShouldNotSpendACode_WhenCredentialsRotatedMidChallenge()
    {
        var challenge = await _service.CreateChallengeAsync(_user, SessionTransport.ApiToken, false, null);
        _user.AuthVersion += 1;

        var act = () => _service.VerifyRecoveryCodeAsync(challenge.Challenge, ValidCode);

        await act.Should().ThrowAsync<UnauthorizedException>();
        _recoveryCodes.Verify(r => r.TryConsumeAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task VerifyRecoveryCodeAsync_ShouldBeRefused_WhenTheChallengeOfferedNoCodes()
    {
        _recoveryCodes.Setup(r => r.HasUnusedAsync(UserId)).ReturnsAsync(false);
        var challenge = await _service.CreateChallengeAsync(_user, SessionTransport.ApiToken, false, null);

        var act = () => _service.VerifyRecoveryCodeAsync(challenge.Challenge, ValidCode);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Recovery codes are not available for this sign-in challenge.");
    }

    [Fact]
    public async Task VerifyRecoveryCodeAsync_ShouldRejectMalformedInput_WithoutCountingAnAttempt()
    {
        var challenge = await _service.CreateChallengeAsync(_user, SessionTransport.ApiToken, false, null);

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var act = () => _service.VerifyRecoveryCodeAsync(challenge.Challenge, "123456");
            await act.Should().ThrowAsync<BadRequestException>();
        }

        // Still usable: malformed input never touched the attempt budget.
        _recoveryCodes.Setup(r => r.TryConsumeAsync(UserId, ValidCode)).ReturnsAsync(true);
        (await _service.VerifyRecoveryCodeAsync(challenge.Challenge, ValidCode)).Should().NotBeNull();
    }
}
