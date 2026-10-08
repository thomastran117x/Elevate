using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.session;

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
        var service = new MfaFactorChangeService(sessionMfa.Object);

        await service.RecordAsync(42, "member@example.com", factor, change);

        sessionMfa.Verify(s => s.ClearUserProofsAsync(42), Times.Once);
    }
}
