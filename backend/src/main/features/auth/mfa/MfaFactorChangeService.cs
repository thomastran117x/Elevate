using backend.main.features.auth.mfa.session;
using backend.main.shared.utilities.logger;

namespace backend.main.features.auth.mfa
{
    public enum MfaFactorKind
    {
        Sms,
        Totp,
    }

    public enum MfaFactorChange
    {
        Enabled,
        Disabled,
        Removed,
    }

    /// <summary>
    /// Follow-up for a completed second-factor change. It runs from the controllers rather than
    /// the enrollment services because the step-up service already depends on TOTP enrollment.
    /// </summary>
    public interface IMfaFactorChangeService
    {
        Task RecordAsync(int userId, string email, MfaFactorKind factor, MfaFactorChange change);
    }

    public sealed class MfaFactorChangeService : IMfaFactorChangeService
    {
        private readonly ISessionMfaVerificationService _sessionMfaVerificationService;

        public MfaFactorChangeService(ISessionMfaVerificationService sessionMfaVerificationService)
        {
            _sessionMfaVerificationService = sessionMfaVerificationService;
        }

        public async Task RecordAsync(int userId, string email, MfaFactorKind factor, MfaFactorChange change)
        {
            // The proof that authorized this change is spent: the next sensitive change, on this
            // or any other session, needs a new verification against the new factor set.
            await _sessionMfaVerificationService.ClearUserProofsAsync(userId);

            Logger.Info($"[MfaFactorAudit] userId={userId} factor={factor} change={change}");
        }
    }
}
