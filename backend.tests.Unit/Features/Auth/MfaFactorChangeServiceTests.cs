using backend.tests.Unit.Features.Auth.Events;
using backend.main.features.auth.events;
using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.session;
using backend.main.features.auth.notifications;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class MfaFactorChangeServiceTests
{
    private readonly RecordingAuthSecurityEventEmitter _securityEvents = new();

    [Theory]
    [InlineData(MfaFactorKind.Sms, MfaFactorChange.Enabled)]
    [InlineData(MfaFactorKind.Sms, MfaFactorChange.Disabled)]
    [InlineData(MfaFactorKind.Totp, MfaFactorChange.Removed)]
    [InlineData(MfaFactorKind.RecoveryCodes, MfaFactorChange.Regenerated)]
    public async Task RecordAsync_ShouldClearStepUpProofsOnEverySession(MfaFactorKind factor, MfaFactorChange change)
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();
        var service = new MfaFactorChangeService(sessionMfa.Object, new Mock<IAuthNotificationService>().Object, _securityEvents);

        await service.RecordAsync(42, "member@example.com", factor, change);

        sessionMfa.Verify(s => s.ClearUserProofsAsync(42), Times.Once);
        var recorded = _securityEvents.Of(AuthSecurityEvents.FactorChanged).Should().ContainSingle().Subject;
        recorded.UserId.Should().Be(42);
        recorded.Outcome.Should().Be(AuthSecurityOutcome.Succeeded);
        recorded.Factor.Should().Be(factor switch
        {
            MfaFactorKind.Sms => AuthSecurityFactor.Sms,
            MfaFactorKind.Totp => AuthSecurityFactor.Totp,
            _ => AuthSecurityFactor.RecoveryCodes,
        });
        recorded.Change.Should().Be(change switch
        {
            MfaFactorChange.Enabled => AuthFactorChange.Enabled,
            MfaFactorChange.Disabled => AuthFactorChange.Disabled,
            MfaFactorChange.Regenerated => AuthFactorChange.Regenerated,
            _ => AuthFactorChange.Removed,
        });
    }

    [Theory]
    [InlineData(MfaFactorKind.Sms, MfaFactorChange.Enabled, "sms", "enabled")]
    [InlineData(MfaFactorKind.Totp, MfaFactorChange.Disabled, "totp", "disabled")]
    [InlineData(MfaFactorKind.Totp, MfaFactorChange.Removed, "totp", "removed")]
    [InlineData(MfaFactorKind.RecoveryCodes, MfaFactorChange.Enabled, "recovery_codes", "enabled")]
    [InlineData(MfaFactorKind.RecoveryCodes, MfaFactorChange.Regenerated, "recovery_codes", "regenerated")]
    public async Task RecordAsync_ShouldNotifyWithOnlyTheFactorAndChange(
        MfaFactorKind factor,
        MfaFactorChange change,
        string expectedFactor,
        string expectedChange)
    {
        var notifications = new Mock<IAuthNotificationService>();
        var service = new MfaFactorChangeService(new Mock<ISessionMfaVerificationService>().Object, notifications.Object, _securityEvents);

        await service.RecordAsync(42, "member@example.com", factor, change);

        notifications.Verify(
            n => n.SendMfaFactorChangedAsync("member@example.com", expectedFactor, expectedChange, null),
            Times.Once);
        notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecordAsync_ShouldRecordTheCommittedChange_EvenWhenProofCleanupFails()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();
        sessionMfa.Setup(s => s.ClearUserProofsAsync(42))
            .ThrowsAsync(new InvalidOperationException("redis unavailable"));
        var service = new MfaFactorChangeService(sessionMfa.Object, new Mock<IAuthNotificationService>().Object, _securityEvents);

        var act = () => service.RecordAsync(42, "member@example.com", MfaFactorKind.Totp, MfaFactorChange.Disabled);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _securityEvents.Of(AuthSecurityEvents.FactorChanged).Should().ContainSingle()
            .Which.Change.Should().Be(AuthFactorChange.Disabled);
    }

    [Fact]
    public async Task RecordAsync_ShouldNotFail_WhenTheNoticeCannotBeSent()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();
        var notifications = new Mock<IAuthNotificationService>();
        notifications
            .Setup(n => n.SendMfaFactorChangedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"));
        var service = new MfaFactorChangeService(sessionMfa.Object, notifications.Object, _securityEvents);

        var act = () => service.RecordAsync(42, "member@example.com", MfaFactorKind.Sms, MfaFactorChange.Removed);

        await act.Should().NotThrowAsync();
        sessionMfa.Verify(s => s.ClearUserProofsAsync(42), Times.Once);
    }
}
