using System.Text.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.events;
using backend.main.features.auth.mfa;
using backend.main.features.auth.oauth.links;
using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.passkeys
{
    /// <summary>The signed-in account a passkey request acts for, from its access token.</summary>
    public sealed record PasskeyAccount(int UserId, string Email, string SessionId, int AuthVersion);

    public interface IPasskeyManagementService
    {
        Task<PasskeysResponse> ListAsync(int userId, CancellationToken cancellationToken = default);

        Task<PasskeyCeremonyOptionsResponse> StartRegistrationAsync(
            PasskeyAccount account,
            string? name,
            CancellationToken cancellationToken = default);

        Task<PasskeyResponse> CompleteRegistrationAsync(
            PasskeyAccount account,
            string ceremonyId,
            JsonElement credential,
            CancellationToken cancellationToken = default);

        Task<PasskeyResponse> RenameAsync(
            int userId,
            int passkeyId,
            string name,
            CancellationToken cancellationToken = default);

        Task RemoveAsync(PasskeyAccount account, int passkeyId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Registers and manages the signed-in account's passkeys. Registration and removal are
    /// factor changes: each emits the change, retires step-up proofs, and emails the owner.
    /// </summary>
    public sealed class PasskeyManagementService : IPasskeyManagementService
    {
        public const string DefaultName = "Passkey";

        private readonly IPasskeyCredentialRepository _passkeys;
        private readonly IAuthUserRepository _users;
        private readonly IUserIdentityLinkRepository _links;
        private readonly IPasskeyCeremonyStore _ceremonies;
        private readonly IWebAuthnRelyingParty _relyingParty;
        private readonly IMfaFactorChangeService _factorChanges;
        private readonly IAuthSecurityEventEmitter _securityEvents;
        private readonly TimeProvider _timeProvider;
        private readonly PasskeyOptions _options;

        public PasskeyManagementService(
            IPasskeyCredentialRepository passkeys,
            IAuthUserRepository users,
            IUserIdentityLinkRepository links,
            IPasskeyCeremonyStore ceremonies,
            IWebAuthnRelyingParty relyingParty,
            IMfaFactorChangeService factorChanges,
            IAuthSecurityEventEmitter securityEvents,
            TimeProvider timeProvider,
            IOptions<PasskeyOptions> options)
        {
            _passkeys = passkeys;
            _users = users;
            _links = links;
            _ceremonies = ceremonies;
            _relyingParty = relyingParty;
            _factorChanges = factorChanges;
            _securityEvents = securityEvents;
            _timeProvider = timeProvider;
            _options = options.Value;
        }

        public async Task<PasskeysResponse> ListAsync(int userId, CancellationToken cancellationToken = default)
        {
            var passkeys = await _passkeys.ListForUserAsync(userId, cancellationToken);
            var hasPassword = (await _users.GetAuthByIdAsync(userId))?.Password != null;
            var linkCount = (await _links.ListForUserAsync(userId, cancellationToken)).Count;
            var canRemove = SignInMethods.RemainAfterRemoving(hasPassword, linkCount, passkeys.Count);

            return new PasskeysResponse
            {
                Passkeys = passkeys.Select(passkey => ToResponse(passkey, canRemove)).ToList(),
                Limit = _options.MaxCredentialsPerUser,
            };
        }

        public async Task<PasskeyCeremonyOptionsResponse> StartRegistrationAsync(
            PasskeyAccount account,
            string? name,
            CancellationToken cancellationToken = default)
        {
            var user = await _users.GetAuthByIdAsync(account.UserId)
                ?? throw new UnauthorizedException("This session cannot be verified. Please sign in again.");
            if (user.IsDisabled)
                throw new ForbiddenException("This account is disabled.");

            var existing = await _passkeys.ListForUserAsync(account.UserId, cancellationToken);
            if (existing.Count >= _options.MaxCredentialsPerUser)
                throw LimitReached();

            var userHandle = await _passkeys.GetOrCreateUserHandleAsync(account.UserId, cancellationToken)
                ?? throw new UnauthorizedException("This session cannot be verified. Please sign in again.");

            // The authenticator shows these when choosing a passkey. The username is the
            // account's sign-in name; the email stands in only for an account without one.
            var userName = string.IsNullOrWhiteSpace(user.Username) ? user.Email : user.Username;
            var optionsJson = _relyingParty.CreateRegistrationOptions(
                userHandle,
                userName,
                string.IsNullOrWhiteSpace(user.Name) ? userName : user.Name,
                existing.Select(p => p.CredentialId).ToList());

            return await CreateCeremonyAsync(new PasskeyCeremony
            {
                Type = PasskeyCeremonyType.Registration,
                OptionsJson = optionsJson,
                UserId = account.UserId,
                SessionId = account.SessionId,
                AuthVersion = account.AuthVersion,
                FriendlyName = NormalizeName(name),
            });
        }

        public async Task<PasskeyResponse> CompleteRegistrationAsync(
            PasskeyAccount account,
            string ceremonyId,
            JsonElement credential,
            CancellationToken cancellationToken = default)
        {
            var ceremony = await _ceremonies.ConsumeAsync(ceremonyId, PasskeyCeremonyType.Registration);
            if (ceremony is null)
                throw RegistrationFailed(account, AuthSecurityReason.Expired);

            // Bound to the account, session, and auth version that started it: a ceremony from
            // another session, or one started before credentials rotated, is refused.
            if (ceremony.UserId != account.UserId || ceremony.SessionId != account.SessionId)
                throw RegistrationFailed(account, AuthSecurityReason.UserMismatch);

            var user = await _users.GetAuthByIdAsync(account.UserId);
            if (user is null || user.IsDisabled || user.AuthVersion != ceremony.AuthVersion
                || user.AuthVersion != account.AuthVersion)
            {
                throw RegistrationFailed(account, AuthSecurityReason.AuthVersionMismatch);
            }

            VerifiedPasskeyRegistration verified;
            try
            {
                verified = await _relyingParty.VerifyRegistrationAsync(
                    ceremony.OptionsJson,
                    credential,
                    async (credentialId, ct) => await _passkeys.FindByCredentialIdAsync(credentialId, ct) is null,
                    cancellationToken);
            }
            catch (WebAuthnVerificationException ex)
            {
                Logger.Warn($"[PasskeyManagementService] Registration refused for userId={account.UserId}: {ex.Reason} {ex.Message}");
                throw RegistrationFailed(
                    account,
                    ex.Reason == WebAuthnFailure.Malformed ? AuthSecurityReason.InvalidRequest : AuthSecurityReason.Rejected);
            }

            var userHandle = await _passkeys.GetOrCreateUserHandleAsync(account.UserId, cancellationToken)
                ?? throw RegistrationFailed(account, AuthSecurityReason.UserMismatch);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var passkey = new PasskeyCredential
            {
                UserId = account.UserId,
                CredentialId = verified.CredentialId,
                PublicKey = verified.PublicKey,
                UserHandle = userHandle,
                SignatureCounter = verified.SignatureCounter,
                AaGuid = verified.AaGuid,
                Transports = Clamp(verified.Transports, PasskeyCredential.TransportsMaxLength),
                AttestationFormat = Clamp(verified.AttestationFormat, PasskeyCredential.AttestationFormatMaxLength),
                IsBackupEligible = verified.IsBackupEligible,
                IsBackedUp = verified.IsBackedUp,
                FriendlyName = ceremony.FriendlyName ?? DefaultName,
                CreatedAtUtc = now,
            };

            if (passkey.CredentialId.Length > PasskeyCredential.CredentialIdMaxBytes)
                throw RegistrationFailed(account, AuthSecurityReason.InvalidRequest);

            switch (await _passkeys.AddAsync(passkey, _options.MaxCredentialsPerUser, cancellationToken))
            {
                case PasskeyAddResult.Added:
                    break;
                case PasskeyAddResult.LimitReached:
                    EmitRegistrationFailure(account.UserId, AuthSecurityReason.LimitExceeded);
                    throw LimitReached();
                case PasskeyAddResult.CredentialTaken:
                    // Registered before, to this account or another. Which one is not said.
                    throw RegistrationFailed(account, AuthSecurityReason.Rejected);
                default:
                    throw RegistrationFailed(account, AuthSecurityReason.UserMismatch);
            }

            await _factorChanges.RecordAsync(account.UserId, account.Email, MfaFactorKind.Passkey, MfaFactorChange.Added);

            var remaining = await ListAsync(account.UserId, cancellationToken);
            return remaining.Passkeys.First(p => p.Id == passkey.Id);
        }

        public async Task<PasskeyResponse> RenameAsync(
            int userId,
            int passkeyId,
            string name,
            CancellationToken cancellationToken = default)
        {
            var normalized = NormalizeName(name)
                ?? throw new BadRequestException("Give the passkey a name.");

            if (!await _passkeys.RenameAsync(userId, passkeyId, normalized, cancellationToken))
                throw new ResourceNotFoundException("Passkey not found.");

            var passkeys = await ListAsync(userId, cancellationToken);
            return passkeys.Passkeys.First(p => p.Id == passkeyId);
        }

        public async Task RemoveAsync(PasskeyAccount account, int passkeyId, CancellationToken cancellationToken = default)
        {
            switch (await _passkeys.RemoveIfAnotherMethodRemainsAsync(account.UserId, passkeyId, cancellationToken))
            {
                case PasskeyRemoveResult.Removed:
                    break;
                case PasskeyRemoveResult.LastSignInMethod:
                    throw PasskeyConflictException.LastSignInMethod();
                default:
                    throw new ResourceNotFoundException("Passkey not found.");
            }

            // A registration in flight was started against the old set; it starts over.
            await _ceremonies.RevokeAllForUserAsync(account.UserId);
            await _factorChanges.RecordAsync(account.UserId, account.Email, MfaFactorKind.Passkey, MfaFactorChange.Removed);
        }

        private async Task<PasskeyCeremonyOptionsResponse> CreateCeremonyAsync(PasskeyCeremony ceremony)
        {
            var ceremonyId = await _ceremonies.CreateAsync(ceremony);
            return ToOptionsResponse(ceremonyId, ceremony);
        }

        internal static PasskeyCeremonyOptionsResponse ToOptionsResponse(string ceremonyId, PasskeyCeremony ceremony)
        {
            using var document = JsonDocument.Parse(ceremony.OptionsJson);
            return new PasskeyCeremonyOptionsResponse
            {
                CeremonyId = ceremonyId,
                PublicKey = document.RootElement.Clone(),
                ExpiresAtUtc = ceremony.ExpiresAtUtc,
            };
        }

        private UnauthorizedException RegistrationFailed(PasskeyAccount account, AuthSecurityReason reason)
        {
            EmitRegistrationFailure(account.UserId, reason);
            return new UnauthorizedException("The passkey could not be registered. Please try again.");
        }

        private void EmitRegistrationFailure(int userId, AuthSecurityReason reason) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.FactorChangeFailed,
                Outcome = AuthSecurityOutcome.Denied,
                Reason = reason,
                Factor = AuthSecurityFactor.Passkey,
                Change = AuthFactorChange.Added,
                Flow = AuthSecurityFlow.MfaEnrollment,
                UserId = userId,
            });

        private static ConflictException LimitReached() =>
            PasskeyConflictException.LimitReached();

        private static string? NormalizeName(string? name)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return null;

            return trimmed.Length <= PasskeyCredential.FriendlyNameMaxLength
                ? trimmed
                : trimmed[..PasskeyCredential.FriendlyNameMaxLength];
        }

        private static string? Clamp(string? value, int maxLength) =>
            value is null || value.Length <= maxLength ? value : value[..maxLength];

        private static PasskeyResponse ToResponse(PasskeyCredential passkey, bool canRemove) => new()
        {
            Id = passkey.Id,
            Name = passkey.FriendlyName,
            Syncable = passkey.IsBackupEligible,
            BackedUp = passkey.IsBackedUp,
            CanRemove = canRemove,
            CreatedAt = passkey.CreatedAtUtc,
            LastUsedAt = passkey.LastUsedAtUtc,
        };
    }
}
