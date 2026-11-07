using backend.main.features.auth.events;
using backend.main.features.auth.mfa.session;
using backend.main.features.auth.notifications;
using backend.main.shared.utilities.logger;

namespace backend.main.features.auth.mfa
{
    public enum MfaFactorKind
    {
        Sms,
        Totp,
        RecoveryCodes,
    }

    public enum MfaFactorChange
    {
        Enabled,
        Disabled,
        Removed,
        Regenerated,
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
        private readonly IAuthNotificationService _notificationService;
        private readonly IAuthSecurityEventEmitter _securityEvents;

        public MfaFactorChangeService(
            ISessionMfaVerificationService sessionMfaVerificationService,
            IAuthNotificationService notificationService,
            IAuthSecurityEventEmitter securityEvents)
        {
            _sessionMfaVerificationService = sessionMfaVerificationService;
            _notificationService = notificationService;
            _securityEvents = securityEvents;
        }

        public async Task RecordAsync(int userId, string email, MfaFactorKind factor, MfaFactorChange change)
        {
            // The change is already committed, so it is recorded first: a failure clearing proofs
            // below must not drop it from the security events.
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.FactorChanged,
                Outcome = AuthSecurityOutcome.Succeeded,
                Factor = factor switch
                {
                    MfaFactorKind.Sms => AuthSecurityFactor.Sms,
                    MfaFactorKind.Totp => AuthSecurityFactor.Totp,
                    _ => AuthSecurityFactor.RecoveryCodes,
                },
                Change = change switch
                {
                    MfaFactorChange.Enabled => AuthFactorChange.Enabled,
                    MfaFactorChange.Disabled => AuthFactorChange.Disabled,
                    MfaFactorChange.Regenerated => AuthFactorChange.Regenerated,
                    _ => AuthFactorChange.Removed,
                },
                UserId = userId,
            });

            // The proof that authorized this change is spent: the next sensitive change, on this
            // or any other session, needs a new verification against the new factor set.
            try
            {
                await _sessionMfaVerificationService.ClearUserProofsAsync(userId);
            }
            catch (Exception ex) when (factor == MfaFactorKind.RecoveryCodes)
            {
                // A regeneration's new codes exist only in the response being built, and the old
                // ones are already gone, so failing here would leave the user with neither. The
                // proofs are retired anyway: the recovery-code set is part of every proof's
                // factor state, which this committed change has already moved.
                Logger.Error($"[MfaFactorChangeService] Could not advance the step-up generation for userId={userId} after a recovery-code change: {ex.GetType().Name}");
            }

            // The change is already committed; a lost notice must not turn it into an error.
            try
            {
                // Names the factor and the change only: a recovery-code notice never carries codes.
                await _notificationService.SendMfaFactorChangedAsync(
                    email,
                    AuthSecurityWireNames.Of(factor),
                    AuthSecurityWireNames.Of(change));
            }
            catch (Exception ex)
            {
                Logger.Error($"[MfaFactorChangeService] Factor-change notice for userId={userId} failed: {ex}");
            }
        }
    }
}
