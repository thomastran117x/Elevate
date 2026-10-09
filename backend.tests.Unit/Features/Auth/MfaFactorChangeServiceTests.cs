using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.session;
using backend.main.features.auth.notifications;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class MfaFactorChangeServiceTests
{
    [Theory]
    [InlineData(MfaFactorKind.Sms, MfaFactorChange.Enabled)]
    [InlineData(MfaFactorKind.Sms, MfaFactorChange.Disabled)]
    [InlineData(MfaFactorKind.Totp, MfaFactorChange.Removed)]
    public async Task RecordAsync_ShouldClearStepUpProofsOnEverySession(MfaFactorKind factor, MfaFactorChange change)
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();
        var service = new MfaFactorChangeService(sessionMfa.Object, new Mock<IAuthNotificationService>().Object);

        await service.RecordAsync(42, "member@example.com", factor, change);

        sessionMfa.Verify(s => s.ClearUserProofsAsync(42), Times.Once);
    }

    [Theory]
    [InlineData(MfaFactorKind.Sms, MfaFactorChange.Enabled, "sms", "enabled")]
    [InlineData(MfaFactorKind.Totp, MfaFactorChange.Disabled, "totp", "disabled")]
    [InlineData(MfaFactorKind.Totp, MfaFactorChange.Removed, "totp", "removed")]
    public async Task RecordAsync_ShouldNotifyWithOnlyTheFactorAndChange(
        MfaFactorKind factor,
        MfaFactorChange change,
        string expectedFactor,
        string expectedChange)
    {
        var notifications = new Mock<IAuthNotificationService>();
        var service = new MfaFactorChangeService(new Mock<ISessionMfaVerificationService>().Object, notifications.Object);

        await service.RecordAsync(42, "member@example.com", factor, change);

        notifications.Verify(
            n => n.SendMfaFactorChangedAsync("member@example.com", expectedFactor, expectedChange, null),
            Times.Once);
        notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecordAsync_ShouldNotFail_WhenTheNoticeCannotBeSent()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();
        var notifications = new Mock<IAuthNotificationService>();
        notifications
            .Setup(n => n.SendMfaFactorChangedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"));
        var service = new MfaFactorChangeService(sessionMfa.Object, notifications.Object);

        var act = () => service.RecordAsync(42, "member@example.com", MfaFactorKind.Sms, MfaFactorChange.Removed);

        await act.Should().NotThrowAsync();
        sessionMfa.Verify(s => s.ClearUserProofsAsync(42), Times.Once);
    }
}
