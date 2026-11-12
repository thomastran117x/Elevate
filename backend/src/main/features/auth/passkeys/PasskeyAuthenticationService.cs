using System.Text.Json;

using backend.main.application.security;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.events;
using backend.main.features.auth.mfa.session;
using backend.main.features.auth.token;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

namespace backend.main.features.auth.passkeys
{
    public interface IPasskeyAuthenticationService
    {
        /// <summary>Options for a username-less passkey sign-in.</summary>
        Task<PasskeyCeremonyOptionsResponse> StartSignInAsync();

        /// <summary>
        /// Verifies the assertion and signs its owner in. A verified passkey is the whole sign-in:
        /// it already proves possession and user verification, so no further challenge follows.
        /// </summary>
        Task<UserToken> CompleteSignInAsync(
            string ceremonyId,
            JsonElement credential,
            SessionTransport transport,
            bool rememberMe,
            CancellationToken cancellationToken = default);

        Task<PasskeyCeremonyOptionsResponse> StartStepUpAsync(
            PasskeyAccount account,
            CancellationToken cancellationToken = default);

        /// <summary>Verifies the assertion and records a step-up proof for the session.</summary>
        Task CompleteStepUpAsync(
            PasskeyAccount account,
            string ceremonyId,
            JsonElement credential,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Passkey sign-in and in-session step-up. Every refusal looks the same to the client, so a
    /// response cannot tell an unknown credential from a bad signature, a replay, or another
    /// account's passkey; the reason goes only to the security events.
    /// </summary>
    public sealed class PasskeyAuthenticationService : IPasskeyAuthenticationService
    {
        private readonly IPasskeyCredentialRepository _passkeys;
        private readonly IAuthUserRepository _users;
        private readonly IPasskeyCeremonyStore _ceremonies;
        private readonly IWebAuthnRelyingParty _relyingParty;
        private readonly IAuthSessionService _sessions;
        private readonly ISessionMfaVerificationService _stepUp;
        private readonly IAuthSecurityEventEmitter _securityEvents;
        private readonly TimeProvider _timeProvider;

        public PasskeyAuthenticationService(
            IPasskeyCredentialRepository passkeys,
            IAuthUserRepository users,
            IPasskeyCeremonyStore ceremonies,
            IWebAuthnRelyingParty relyingParty,
            IAuthSessionService sessions,
            ISessionMfaVerificationService stepUp,
            IAuthSecurityEventEmitter securityEvents,
            TimeProvider timeProvider)
        {
            _passkeys = passkeys;
            _users = users;
            _ceremonies = ceremonies;
            _relyingParty = relyingParty;
            _sessions = sessions;
            _stepUp = stepUp;
            _securityEvents = securityEvents;
            _timeProvider = timeProvider;
        }

        public async Task<PasskeyCeremonyOptionsResponse> StartSignInAsync()
        {
            var ceremony = new PasskeyCeremony
            {
                Type = PasskeyCeremonyType.SignIn,
                OptionsJson = _relyingParty.CreateAssertionOptions([]),
            };
            var ceremonyId = await _ceremonies.CreateAsync(ceremony);
            return PasskeyManagementService.ToOptionsResponse(ceremonyId, ceremony);
        }

        public async Task<UserToken> CompleteSignInAsync(
            string ceremonyId,
            JsonElement credential,
            SessionTransport transport,
            bool rememberMe,
            CancellationToken cancellationToken = default)
        {
            var ceremony = await _ceremonies.ConsumeAsync(ceremonyId, PasskeyCeremonyType.SignIn);
            if (ceremony is null)
                throw SignInFailed(null, AuthSecurityReason.Expired);

            var candidate = ReadCandidate(credential)
                ?? throw SignInFailed(null, AuthSecurityReason.InvalidRequest);

            // Sign-in names no account, so the user handle is what binds the passkey to one.
            if (candidate.UserHandle is not { Length: > 0 })
                throw SignInFailed(null, AuthSecurityReason.InvalidRequest);

            var stored = await _passkeys.FindByCredentialIdAsync(candidate.CredentialId, cancellationToken);
            if (stored is null)
                throw SignInFailed(null, AuthSecurityReason.CredentialNotFound);

            if (!candidate.UserHandle.AsSpan().SequenceEqual(stored.UserHandle))
                throw SignInFailed(stored.UserId, AuthSecurityReason.UserMismatch);

            var record = await _users.GetAuthByIdAsync(stored.UserId);
            if (record is null)
                throw SignInFailed(stored.UserId, AuthSecurityReason.CredentialNotFound);

            var failure = await VerifyAsync(ceremony, credential, stored, candidate, cancellationToken);
            if (failure is { } reason)
                throw SignInFailed(stored.UserId, reason);

            // Checked only once the passkey is proven, so whether an account is disabled is told
            // to its owner and no one else.
            if (record.IsDisabled)
            {
                _securityEvents.Emit(new AuthSecurityEvent
                {
                    Definition = AuthSecurityEvents.SignInFailed,
                    Outcome = AuthSecurityOutcome.Denied,
                    Reason = AuthSecurityReason.AccountDisabled,
                    Method = AuthSecurityMethod.Passkey,
                    Flow = AuthSecurityFlow.Login,
                    UserId = record.Id,
                });
                throw new ForbiddenException("This account is disabled.");
            }

            var user = new User
            {
                Id = record.Id,
                Email = record.Email,
                Password = record.Password,
                Usertype = AuthRoles.NormalizeStored(record.Usertype),
                IsDisabled = record.IsDisabled,
                AuthVersion = record.AuthVersion,
            };

            return _securityEvents.SignedIn(
                await _sessions.IssueAsync(user, transport, rememberMe: rememberMe),
                AuthSecurityMethod.Passkey,
                AuthSecurityFlow.Login);
        }

        public async Task<PasskeyCeremonyOptionsResponse> StartStepUpAsync(
            PasskeyAccount account,
            CancellationToken cancellationToken = default)
        {
            var passkeys = await _passkeys.ListForUserAsync(account.UserId, cancellationToken);
            if (passkeys.Count == 0)
                throw new BadRequestException("The requested verification method is unavailable.");

            // Captured now, before anything is verified, so a factor change that completes while
            // the user is at the authenticator retires the proof this ceremony would record.
            var snapshot = await _stepUp.CaptureSnapshotAsync(account.UserId);
            var ceremony = new PasskeyCeremony
            {
                Type = PasskeyCeremonyType.StepUp,
                OptionsJson = _relyingParty.CreateAssertionOptions(passkeys.Select(p => p.CredentialId).ToList()),
                UserId = account.UserId,
                SessionId = account.SessionId,
                AuthVersion = account.AuthVersion,
                StepUpGeneration = snapshot.Generation,
                FactorState = snapshot.FactorState,
            };
            var ceremonyId = await _ceremonies.CreateAsync(ceremony);
            return PasskeyManagementService.ToOptionsResponse(ceremonyId, ceremony);
        }

        public async Task CompleteStepUpAsync(
            PasskeyAccount account,
            string ceremonyId,
            JsonElement credential,
            CancellationToken cancellationToken = default)
        {
            var ceremony = await _ceremonies.ConsumeAsync(ceremonyId, PasskeyCeremonyType.StepUp);
            if (ceremony is null)
                throw StepUpFailed(account, AuthSecurityReason.Expired);

            if (ceremony.UserId != account.UserId || ceremony.SessionId != account.SessionId)
                throw StepUpFailed(account, AuthSecurityReason.UserMismatch);

            if (ceremony.AuthVersion != account.AuthVersion
                || ceremony.StepUpGeneration is not { } generation
                || ceremony.FactorState is not { } factorState)
            {
                throw StepUpFailed(account, AuthSecurityReason.AuthVersionMismatch);
            }

            var candidate = ReadCandidate(credential)
                ?? throw StepUpFailed(account, AuthSecurityReason.InvalidRequest);

            // Only the caller's own passkeys were offered, and only they are accepted: another
            // account's passkey cannot step up this session.
            var stored = await _passkeys.FindByCredentialIdAsync(candidate.CredentialId, cancellationToken);
            if (stored is null)
                throw StepUpFailed(account, AuthSecurityReason.CredentialNotFound);

            if (stored.UserId != account.UserId
                || (candidate.UserHandle is { Length: > 0 } handle && !handle.AsSpan().SequenceEqual(stored.UserHandle)))
            {
                throw StepUpFailed(account, AuthSecurityReason.UserMismatch);
            }

            var failure = await VerifyAsync(ceremony, credential, stored, candidate, cancellationToken);
            if (failure is { } reason)
                throw StepUpFailed(account, reason);

            await _stepUp.RecordPasskeyProofAsync(
                account.UserId,
                account.SessionId,
                account.AuthVersion,
                new StepUpSnapshot(generation, factorState));
        }

        private PasskeyAssertionCandidate? ReadCandidate(JsonElement credential)
        {
            try
            {
                return _relyingParty.ReadAssertion(credential);
            }
            catch (WebAuthnVerificationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Verifies the assertion and records the use. Returns why it failed, or null.
        /// </summary>
        /// <remarks>
        /// The counter policy follows WebAuthn Level 3: a backup-eligible (synced) passkey has
        /// copies on several devices, so its counter may stay at zero or arrive out of order; a
        /// regression there is recorded but does not refuse the sign-in. A device-bound passkey
        /// has one counter, so a regression refuses that assertion. Neither disables the passkey.
        /// </remarks>
        private async Task<AuthSecurityReason?> VerifyAsync(
            PasskeyCeremony ceremony,
            JsonElement credential,
            PasskeyCredential stored,
            PasskeyAssertionCandidate candidate,
            CancellationToken cancellationToken)
        {
            // BE is fixed when a credential is created, so a different value means a different
            // authenticator presenting this credential id.
            if (candidate.IsBackupEligible != stored.IsBackupEligible)
                return AuthSecurityReason.Rejected;

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            VerifiedPasskeyAssertion verified;
            try
            {
                verified = await _relyingParty.VerifyAssertionAsync(
                    ceremony.OptionsJson,
                    credential,
                    stored,
                    enforceCounter: !stored.IsBackupEligible,
                    cancellationToken);
            }
            catch (WebAuthnVerificationException ex) when (ex.Reason == WebAuthnFailure.CounterRegressed)
            {
                await _passkeys.FlagCounterAnomalyAsync(stored.Id, now, cancellationToken);
                EmitCounterAnomaly(stored, accepted: false);
                return AuthSecurityReason.CounterRegressed;
            }
            catch (WebAuthnVerificationException ex)
            {
                Logger.Warn($"[PasskeyAuthenticationService] Assertion refused for passkeyId={stored.Id}: {ex.Reason} {ex.Message}");
                return ex.Reason == WebAuthnFailure.Malformed
                    ? AuthSecurityReason.InvalidRequest
                    : AuthSecurityReason.Rejected;
            }

            var anomaly = stored.IsBackupEligible
                && (verified.SignatureCounter != 0 || stored.SignatureCounter != 0)
                && verified.SignatureCounter <= stored.SignatureCounter;
            if (anomaly)
                EmitCounterAnomaly(stored, accepted: true);

            await _passkeys.RecordUseAsync(
                stored.Id,
                verified.SignatureCounter,
                verified.IsBackedUp,
                now,
                anomaly,
                cancellationToken);
            return null;
        }

        private void EmitCounterAnomaly(PasskeyCredential stored, bool accepted) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.PasskeyCounterAnomaly,
                Outcome = accepted ? AuthSecurityOutcome.Succeeded : AuthSecurityOutcome.Denied,
                Reason = AuthSecurityReason.CounterRegressed,
                Method = AuthSecurityMethod.Passkey,
                UserId = stored.UserId,
            });

        private PasskeyAuthenticationFailedException SignInFailed(int? userId, AuthSecurityReason reason)
        {
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.SignInFailed,
                Outcome = AuthSecurityOutcome.Denied,
                Reason = reason,
                Method = AuthSecurityMethod.Passkey,
                Flow = AuthSecurityFlow.Login,
                UserId = userId,
            });
            return new PasskeyAuthenticationFailedException();
        }

        private PasskeyAuthenticationFailedException StepUpFailed(PasskeyAccount account, AuthSecurityReason reason)
        {
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.StepUpFailed,
                Outcome = AuthSecurityOutcome.Denied,
                Reason = reason,
                Method = AuthSecurityMethod.Passkey,
                Flow = AuthSecurityFlow.SessionStepUp,
                UserId = account.UserId,
                SessionId = account.SessionId,
            });
            return new PasskeyAuthenticationFailedException();
        }
    }

    /// <summary>The one refusal every failed passkey sign-in or step-up returns.</summary>
    public sealed class PasskeyAuthenticationFailedException : AppException
    {
        public const string Code = "AUTHENTICATION_FAILED";

        public PasskeyAuthenticationFailedException()
            : base("Passkey verification failed. Please try again.", StatusCodes.Status401Unauthorized, Code)
        {
        }
    }
}
