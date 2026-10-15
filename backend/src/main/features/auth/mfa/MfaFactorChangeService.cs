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
                Factor = factor == MfaFactorKind.Sms ? AuthSecurityFactor.Sms : AuthSecurityFactor.Totp,
                Change = change switch
                {
                    MfaFactorChange.Enabled => AuthFactorChange.Enabled,
                    MfaFactorChange.Disabled => AuthFactorChange.Disabled,
                    _ => AuthFactorChange.Removed,
                },
                UserId = userId,
            });

            // The proof that authorized this change is spent: the next sensitive change, on this
            // or any other session, needs a new verification against the new factor set.
            await _sessionMfaVerificationService.ClearUserProofsAsync(userId);

            // The change is already committed; a lost notice must not turn it into an error.
            try
            {
                await _notificationService.SendMfaFactorChangedAsync(
                    email,
                    factor.ToString().ToLowerInvariant(),
                    change.ToString().ToLowerInvariant());
            }
            catch (Exception ex)
            {
                Logger.Error($"[MfaFactorChangeService] Factor-change notice for userId={userId} failed: {ex}");
            }
        }
    }
}
