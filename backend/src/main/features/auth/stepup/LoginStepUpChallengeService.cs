using System.Security.Cryptography;
using System.Text;

using backend.main.application.environment;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.device;
using backend.main.features.auth.events;
using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.recovery;
using backend.main.features.auth.mfa.totp;
using backend.main.features.auth.notifications;
using backend.main.features.auth.token;
using backend.main.features.cache;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;
using backend.main.shared.utilities;
using backend.main.shared.utilities.logger;

using Microsoft.IdentityModel.Tokens;

using Newtonsoft.Json;

namespace backend.main.features.auth.stepup
{
    public sealed class LoginStepUpChallengeService : ILoginStepUpChallengeService
    {
        private static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan CompletionLockTtl = TimeSpan.FromSeconds(30);
        private const int MaxSmsAttempts = 5;
        private const int MaxTotpAttempts = 5;
        private const int MaxRecoveryCodeAttempts = 5;
        private const int MaxMethodSends = 3;
        private const int MaxUserSends = 6;
        private const int MaxIpSends = 20;
        private const string EmailMethod = "email";
        private const string SmsMethod = "sms";
        private const string TotpMethod = "totp";
        private const string RecoveryCodeMethod = "recovery_code";

        /// <summary>
        /// Prefix of step-up email tokens. Step-up and new-device links share one endpoint, so the
        /// prefix is what tells an unknown or used step-up link from a device link. The dot never
        /// occurs in the Base64Url device tokens.
        /// </summary>
        internal const string EmailTokenPrefix = "su.";

        private readonly ICacheService _cacheService;
        private readonly IAuthNotificationService _notificationService;
        private readonly IMfaEnrollmentRepository _mfaEnrollmentRepository;
        private readonly ITotpMfaEnrollmentService _totpMfaEnrollmentService;
        private readonly IDeviceTrustService _deviceTrustService;
        private readonly IAuthUserRepository _userRepository;
        private readonly IAuthSessionService _authSessionService;
        private readonly ITokenService _tokenService;
        private readonly ClientRequestInfo _requestInfo;
        private readonly IAuthSecurityEventEmitter _securityEvents;
        private readonly IRecoveryCodeService _recoveryCodeService;

        public LoginStepUpChallengeService(
            ICacheService cacheService,
            IAuthNotificationService notificationService,
            IMfaEnrollmentRepository mfaEnrollmentRepository,
            ITotpMfaEnrollmentService totpMfaEnrollmentService,
            IDeviceTrustService deviceTrustService,
            IAuthUserRepository userRepository,
            IAuthSessionService authSessionService,
            ITokenService tokenService,
            ClientRequestInfo requestInfo,
            IAuthSecurityEventEmitter securityEvents,
            IRecoveryCodeService recoveryCodeService
        )
        {
            _cacheService = cacheService;
            _notificationService = notificationService;
            _mfaEnrollmentRepository = mfaEnrollmentRepository;
            _totpMfaEnrollmentService = totpMfaEnrollmentService;
            _deviceTrustService = deviceTrustService;
            _userRepository = userRepository;
            _authSessionService = authSessionService;
            _tokenService = tokenService;
            _requestInfo = requestInfo;
            _securityEvents = securityEvents;
            _recoveryCodeService = recoveryCodeService;
        }

        public async Task<LoginStepUpChallengeResponse> CreateChallengeAsync(
            User user,
            SessionTransport transport,
            bool rememberMe,
            string? returnUrl
        )
        {
            try
            {
                var smsEnrollment = await _mfaEnrollmentRepository.GetByUserIdAsync(user.Id);
                var totpEnrollment = await _totpMfaEnrollmentService.GetEnrollmentAsync(user.Id);
                var rawChallenge = CreateRandomToken();
                var state = new PendingStepUpState
                {
                    PendingId = CreateRandomToken(),
                    ChallengeHash = CryptoHelper.HashToken(rawChallenge),
                    UserId = user.Id,
                    AuthVersion = user.AuthVersion,
                    Email = user.Email,
                    PhoneNumber = CanUseSms(smsEnrollment) ? smsEnrollment!.PhoneNumber : null,
                    HasTotp = totpEnrollment?.IsTotpMfaEnabled == true && EnvironmentSetting.AuthTotpMfaStepUpEnabled,
                    HasRecoveryCodes = await _recoveryCodeService.HasUnusedAsync(user.Id),
                    Transport = transport,
                    RememberMe = rememberMe,
                    TrustedDeviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                    DeviceType = _requestInfo.DeviceType,
                    ClientName = _requestInfo.ClientName,
                    IpAddress = _requestInfo.IpAddress,
                    ReturnPath = NormalizeReturnPath(returnUrl),
                    ExpiresAtUtc = DateTime.UtcNow.Add(ChallengeTtl),
                    Sms = new DeliveryState(),
                    EmailDelivery = new DeliveryState()
                };

                if (!await PersistStateAsync(state))
                    throw new NotAvailableException();

                return ToChallengeResponse(rawChallenge, state);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[LoginStepUpChallengeService] CreateChallengeAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<StartLoginStepUpResponse> StartAsync(string challenge, string method)
        {
            try
            {
                var normalizedMethod = NormalizeMethod(method);
                var challengeHash = CryptoHelper.HashToken(challenge);
                var state = await GetStateByHashAsync(challengeHash)
                    ?? throw new UnauthorizedException("Invalid or expired sign-in verification challenge.");

                EnsureMethodAllowed(state, normalizedMethod);
                var now = DateTime.UtcNow;

                // TOTP and recovery codes need no delivery: return the existing challenge as is.
                if (normalizedMethod is TotpMethod or RecoveryCodeMethod)
                {
                    return new StartLoginStepUpResponse
                    {
                        Challenge = challenge,
                        ExpiresAtUtc = state.ExpiresAtUtc,
                        SelectedMethod = normalizedMethod,
                        MaskedDestination = normalizedMethod == TotpMethod ? "authenticator app" : "recovery code",
                        CooldownEndsAtUtc = DateTime.MinValue,
                        AvailableMethods = GetAvailableMethods(state),
                        MaskedPhone = CanUseSms(state) ? PhoneNumberFormatter.Mask(state.PhoneNumber!) : null,
                        MaskedEmail = PhoneNumberFormatter.MaskEmail(state.Email)
                    };
                }

                try
                {
                    EnforceCooldown(state, normalizedMethod, now);
                }
                catch (TooManyRequestException)
                {
                    EmitThrottled(state.UserId, AuthThrottleDimension.Attempts);
                    throw;
                }

                await EnforceSendCapsAsync(state);

                var previousChallengeHash = state.ChallengeHash;
                var previousEmailTokenHash = state.EmailTokenHash;
                var nextChallenge = CreateRandomToken();
                state.ChallengeHash = CryptoHelper.HashToken(nextChallenge);
                state.SmsCodeHash = null;
                state.EmailTokenHash = null;
                state.SmsFailedAttempts = 0;

                var delivery = GetDeliveryState(state, normalizedMethod)!;
                delivery.SendCount += 1;
                delivery.CooldownEndsAtUtc = now.Add(ResendCooldown);

                string maskedDestination;
                string? smsCode = null;
                string? emailToken = null;

                if (normalizedMethod == SmsMethod)
                {
                    smsCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
                    state.SmsCodeHash = HashCode(state.PendingId, state.ChallengeHash, smsCode);
                    maskedDestination = PhoneNumberFormatter.Mask(state.PhoneNumber!);
                }
                else
                {
                    emailToken = EmailTokenPrefix + CreateRandomToken();
                    state.EmailTokenHash = CryptoHelper.HashToken(emailToken);
                    maskedDestination = PhoneNumberFormatter.MaskEmail(state.Email);
                }

                if (!await PersistStateAsync(state, previousChallengeHash, previousEmailTokenHash))
                    throw new NotAvailableException();

                try
                {
                    if (normalizedMethod == SmsMethod)
                    {
                        await _notificationService.SendSmsMfaAsync(
                            state.PhoneNumber!,
                            smsCode!,
                            nextChallenge,
                            state.ExpiresAtUtc,
                            "sign-in verification"
                        );
                    }
                    else
                    {
                        await _notificationService.SendDeviceVerificationAsync(state.Email, emailToken!);
                    }
                }
                catch
                {
                    // Restore previous challenge state so user can retry without a full re-login,
                    // and refund the send-cap counters consumed for this failed attempt.
                    var rotatedChallengeHash = state.ChallengeHash;
                    var rotatedEmailTokenHash = state.EmailTokenHash;
                    state.ChallengeHash = previousChallengeHash;
                    state.SmsCodeHash = null;
                    state.EmailTokenHash = previousEmailTokenHash;
                    state.SmsFailedAttempts = 0;
                    delivery.SendCount -= 1;
                    delivery.CooldownEndsAtUtc = null;
                    await _cacheService.DecrementAsync(UserSendKey(state.UserId));
                    await _cacheService.DecrementAsync(IpSendKey(_requestInfo.IpAddress));
                    await PersistStateAsync(state, rotatedChallengeHash, rotatedEmailTokenHash);
                    throw;
                }

                return new StartLoginStepUpResponse
                {
                    Challenge = nextChallenge,
                    ExpiresAtUtc = state.ExpiresAtUtc,
                    SelectedMethod = normalizedMethod,
                    MaskedDestination = maskedDestination,
                    CooldownEndsAtUtc = delivery.CooldownEndsAtUtc ?? now.Add(ResendCooldown),
                    AvailableMethods = GetAvailableMethods(state),
                    MaskedPhone = CanUseSms(state) ? PhoneNumberFormatter.Mask(state.PhoneNumber!) : null,
                    MaskedEmail = PhoneNumberFormatter.MaskEmail(state.Email)
                };
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[LoginStepUpChallengeService] StartAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<AuthenticatedSessionResult> VerifySmsAsync(string challenge, string code)
        {
            try
            {
                var challengeHash = CryptoHelper.HashToken(challenge);
                var state = await GetStateByHashAsync(challengeHash)
                    ?? throw StepUpRejected(
                        AuthSecurityMethod.Sms,
                        AuthSecurityReason.Expired,
                        "Invalid or expired sign-in verification challenge.");

                if (string.IsNullOrWhiteSpace(state.SmsCodeHash))
                    throw StepUpRejected(
                        AuthSecurityMethod.Sms,
                        AuthSecurityReason.InvalidState,
                        "Invalid or expired sign-in verification code.",
                        state.UserId);

                var expectedHash = HashCode(state.PendingId, state.ChallengeHash, code);
                if (!CryptoHelper.FixedTimeEquals(state.SmsCodeHash, expectedHash))
                {
                    state.SmsFailedAttempts += 1;
                    if (state.SmsFailedAttempts >= MaxSmsAttempts)
                    {
                        await DeleteStateAsync(state);
                    }
                    else if (!await PersistStateAsync(state, challengeHash, state.EmailTokenHash))
                    {
                        throw new NotAvailableException();
                    }

                    throw StepUpRejected(
                        AuthSecurityMethod.Sms,
                        state.SmsFailedAttempts >= MaxSmsAttempts
                            ? AuthSecurityReason.AttemptsExhausted
                            : AuthSecurityReason.InvalidCode,
                        "Invalid or expired sign-in verification code.",
                        state.UserId);
                }

                return await CompletePendingChallengeAsync(state, AuthSecurityMethod.Sms);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[LoginStepUpChallengeService] VerifySmsAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<AuthenticatedSessionResult> VerifyTotpAsync(string challenge, string code)
        {
            try
            {
                var challengeHash = CryptoHelper.HashToken(challenge);
                var state = await GetStateByHashAsync(challengeHash)
                    ?? throw StepUpRejected(
                        AuthSecurityMethod.Totp,
                        AuthSecurityReason.Expired,
                        "Invalid or expired sign-in verification challenge.");

                if (!state.HasTotp)
                    throw StepUpRejected(
                        AuthSecurityMethod.Totp,
                        AuthSecurityReason.InvalidState,
                        "TOTP MFA is not available for this sign-in challenge.",
                        state.UserId);

                try
                {
                    await _totpMfaEnrollmentService.VerifyPersistedCodeAsync(state.UserId, code);
                }
                catch (UnauthorizedException)
                {
                    state.TotpFailedAttempts += 1;
                    if (state.TotpFailedAttempts >= MaxTotpAttempts)
                    {
                        await DeleteStateAsync(state);
                    }
                    else if (!await PersistStateAsync(state, challengeHash, state.EmailTokenHash))
                    {
                        throw new NotAvailableException();
                    }

                    EmitStepUpFailed(
                        AuthSecurityMethod.Totp,
                        state.TotpFailedAttempts >= MaxTotpAttempts
                            ? AuthSecurityReason.AttemptsExhausted
                            : AuthSecurityReason.InvalidCode,
                        state.UserId);
                    throw;
                }
                catch (BadRequestException)
                {
                    // A malformed code is refused before it is checked, so it costs no attempt.
                    EmitStepUpFailed(AuthSecurityMethod.Totp, AuthSecurityReason.InvalidRequest, state.UserId);
                    throw;
                }

                return await CompletePendingChallengeAsync(state, AuthSecurityMethod.Totp);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[LoginStepUpChallengeService] VerifyTotpAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<AuthenticatedSessionResult> VerifyRecoveryCodeAsync(string challenge, string code)
        {
            try
            {
                var challengeHash = CryptoHelper.HashToken(challenge);
                var state = await GetStateByHashAsync(challengeHash)
                    ?? throw StepUpRejected(
                        AuthSecurityMethod.RecoveryCode,
                        AuthSecurityReason.Expired,
                        "Invalid or expired sign-in verification challenge.");

                if (!state.HasRecoveryCodes)
                    throw StepUpRejected(
                        AuthSecurityMethod.RecoveryCode,
                        AuthSecurityReason.InvalidState,
                        "Recovery codes are not available for this sign-in challenge.",
                        state.UserId);

                // Input that cannot be a code is refused before it is checked, so it costs no attempt.
                if (!RecoveryCodeFormat.TryNormalize(code, out _))
                {
                    EmitStepUpFailed(AuthSecurityMethod.RecoveryCode, AuthSecurityReason.InvalidRequest, state.UserId);
                    throw new BadRequestException("Enter a recovery code in the format shown when it was generated.");
                }

                RecoveryCodeSpend? spend = null;
                var result = await CompletePendingChallengeAsync(
                    state,
                    AuthSecurityMethod.RecoveryCode,
                    beforeIssue: async () => spend = await ConsumeRecoveryCodeAsync(state.UserId, challengeHash, code),
                    // No session came of it, so the code goes back rather than being burned.
                    onIssueFailed: () => spend is null
                        ? Task.CompletedTask
                        : _recoveryCodeService.ReleaseAsync(state.UserId, spend),
                    // A recovery code is for when the usual factor is gone; it signs this one
                    // session in but does not let the device skip verification for 90 days.
                    trustDevice: false);

                // Recorded only once the sign-in it paid for exists; a released code was not used.
                _securityEvents.Emit(new AuthSecurityEvent
                {
                    Definition = AuthSecurityEvents.RecoveryCodeUsed,
                    Outcome = AuthSecurityOutcome.Succeeded,
                    Method = AuthSecurityMethod.RecoveryCode,
                    Flow = AuthSecurityFlow.LoginStepUp,
                    UserId = state.UserId,
                    SessionId = result.UserToken.token.SessionId,
                });
                return result;
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[LoginStepUpChallengeService] VerifyRecoveryCodeAsync failed: {ex.GetType().Name}");
                throw new InternalServerErrorException();
            }
        }

        /// <summary>
        /// Runs under the completion lock. The challenge is read again here rather than trusted
        /// from before the lock: another attempt may have counted a failure or discarded the
        /// challenge since, and writing back the older copy would undo that.
        /// </summary>
        private async Task<RecoveryCodeSpend> ConsumeRecoveryCodeAsync(int userId, string challengeHash, string code)
        {
            var current = await GetStateByHashAsync(challengeHash)
                ?? throw StepUpRejected(
                    AuthSecurityMethod.RecoveryCode,
                    AuthSecurityReason.Expired,
                    "Invalid or expired sign-in verification challenge.",
                    userId);

            var spend = await _recoveryCodeService.TryConsumeAsync(current.UserId, code);
            if (spend is not null)
                return spend;

            current.RecoveryFailedAttempts += 1;
            if (current.RecoveryFailedAttempts >= MaxRecoveryCodeAttempts)
            {
                await DeleteStateAsync(current);
            }
            else if (!await PersistStateAsync(current, challengeHash, current.EmailTokenHash))
            {
                throw new NotAvailableException();
            }

            throw StepUpRejected(
                AuthSecurityMethod.RecoveryCode,
                current.RecoveryFailedAttempts >= MaxRecoveryCodeAttempts
                    ? AuthSecurityReason.AttemptsExhausted
                    : AuthSecurityReason.InvalidCode,
                "Invalid or already used recovery code.",
                current.UserId);
        }

        public async Task<AuthenticatedSessionResult?> TryVerifyEmailAsync(string token)
        {
            try
            {
                var tokenHash = CryptoHelper.HashToken(token);
                var json = await _cacheService.GetValueAsync(EmailTokenKey(tokenHash));
                if (string.IsNullOrWhiteSpace(json))
                {
                    // Only a step-up link carries the prefix, so an expired or used one is refused here,
                    // as a step-up, with the same response the device-link path would give.
                    if (!token.StartsWith(EmailTokenPrefix, StringComparison.Ordinal))
                        return null;

                    throw StepUpRejected(
                        AuthSecurityMethod.Email,
                        AuthSecurityReason.Expired,
                        "Invalid or expired device verification token.");
                }

                var state = JsonConvert.DeserializeObject<PendingStepUpState>(json)
                    ?? throw StepUpRejected(
                        AuthSecurityMethod.Email,
                        AuthSecurityReason.InvalidState,
                        "Invalid or expired device verification token.");

                if (!string.Equals(state.EmailTokenHash, tokenHash, StringComparison.Ordinal))
                    throw StepUpRejected(
                        AuthSecurityMethod.Email,
                        AuthSecurityReason.Expired,
                        "Invalid or expired device verification token.",
                        state.UserId);

                return await CompletePendingChallengeAsync(state, AuthSecurityMethod.Email);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[LoginStepUpChallengeService] TryVerifyEmailAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        /// <param name="beforeIssue">
        /// Runs under the completion lock once the account and challenge have been checked, and
        /// before any session exists; throwing from it refuses the sign-in.
        /// </param>
        /// <param name="onIssueFailed">
        /// Runs when <paramref name="beforeIssue"/> succeeded but the sign-in then failed, so
        /// whatever it spent can be handed back.
        /// </param>
        /// <param name="trustDevice">Whether a completed sign-in also trusts the device.</param>
        private async Task<AuthenticatedSessionResult> CompletePendingChallengeAsync(
            PendingStepUpState state,
            AuthSecurityMethod method,
            Func<Task>? beforeIssue = null,
            Func<Task>? onIssueFailed = null,
            bool trustDevice = true)
        {
            var lockValue = CreateRandomToken();
            var lockKey = CompletionLockKey(state.PendingId);
            if (!await _cacheService.AcquireLockAsync(lockKey, lockValue, CompletionLockTtl))
                throw StepUpRejected(
                    method,
                    AuthSecurityReason.Replayed,
                    "This sign-in verification challenge has already been used.",
                    state.UserId);

            try
            {
                var user = await _userRepository.GetUserAsync(state.UserId)
                    ?? throw new ResourceNotFoundException($"User with ID {state.UserId} not found.");
                if (user.IsDisabled)
                {
                    _securityEvents.Emit(new AuthSecurityEvent
                    {
                        Definition = AuthSecurityEvents.SignInFailed,
                        Outcome = AuthSecurityOutcome.Denied,
                        Reason = AuthSecurityReason.AccountDisabled,
                        Method = method,
                        Flow = AuthSecurityFlow.LoginStepUp,
                        UserId = user.Id,
                    });
                    throw new ForbiddenException("This account is disabled.");
                }

                // Revoking sessions cannot reach a challenge that is mid-flight: it holds no
                // session yet. Without this check, a password or email change that signed every
                // device out would still be followed by a challenge completing into a fresh
                // session for whoever started it.
                if (user.AuthVersion != state.AuthVersion)
                {
                    await DeleteStateAsync(state);
                    throw StepUpRejected(
                        method,
                        AuthSecurityReason.AuthVersionMismatch,
                        "This sign-in verification challenge is no longer valid. Please sign in again.",
                        state.UserId);
                }

                if (beforeIssue is not null)
                    await beforeIssue();

                try
                {
                    var userToken = await _authSessionService.IssueAsync(
                        user,
                        state.Transport,
                        rememberMe: state.RememberMe
                    );

                    // The check above is not enough on its own: a rotation can commit between it and
                    // the line above, running its revocation before this session existed. Trust is
                    // also the one thing this path grants that outlives the session - 90 days of
                    // skipping MFA, and an email change leaves the password intact - so it must never
                    // land for a sign-in that is then rejected. Both are settled here, once the session
                    // exists: the trust is written only if the version still matches, compared under
                    // the account's row lock, so no rotation can commit between that comparison and
                    // the write. A sign-in that grants no trust has nothing here that outlives its
                    // session, which is itself bound to the auth version and refused once it moves.
                    var trusted = !trustDevice || await _deviceTrustService.TrustAsync(
                        state.UserId,
                        state.AuthVersion,
                        state.TrustedDeviceId,
                        state.DeviceType,
                        state.ClientName,
                        state.IpAddress
                    );
                    if (!trusted)
                    {
                        // Revoking the account's sessions rather than just this one: a rotation has
                        // happened, so everything is meant to be gone, and this is idempotent with the
                        // revocation the rotation already ran.
                        await _tokenService.RevokeAllRefreshSessionsAsync(state.UserId, AuthSecurityReason.AuthVersionMismatch);
                        await DeleteStateAsync(state);
                        throw StepUpRejected(
                            method,
                            AuthSecurityReason.AuthVersionMismatch,
                            "This sign-in verification challenge is no longer valid. Please sign in again.",
                            state.UserId);
                    }

                    await DeleteStateAsync(state);

                    _securityEvents.Emit(new AuthSecurityEvent
                    {
                        Definition = AuthSecurityEvents.StepUpSucceeded,
                        Outcome = AuthSecurityOutcome.Succeeded,
                        Method = method,
                        Flow = AuthSecurityFlow.LoginStepUp,
                        UserId = state.UserId,
                        SessionId = userToken.token.SessionId,
                    });

                    return new AuthenticatedSessionResult
                    {
                        UserToken = _securityEvents.SignedIn(userToken, method, AuthSecurityFlow.LoginStepUp),
                        ReturnPath = state.ReturnPath
                    };
                }
                catch when (onIssueFailed is not null)
                {
                    await onIssueFailed!();
                    throw;
                }
            }
            finally
            {
                await _cacheService.ReleaseLockAsync(lockKey, lockValue);
            }
        }

        private async Task EnforceSendCapsAsync(PendingStepUpState state)
        {
            await IncrementWithLimitAsync(
                UserSendKey(state.UserId),
                MaxUserSends,
                state.UserId,
                AuthThrottleDimension.Account,
                "Too many sign-in verification deliveries have been requested for this account. Please try again later."
            );
            await IncrementWithLimitAsync(
                IpSendKey(_requestInfo.IpAddress),
                MaxIpSends,
                state.UserId,
                AuthThrottleDimension.SourceIp,
                "Too many sign-in verification deliveries have been requested from this IP. Please try again later."
            );
        }

        private async Task IncrementWithLimitAsync(
            string key,
            int limit,
            int userId,
            AuthThrottleDimension dimension,
            string message)
        {
            var count = await _cacheService.IncrementAsync(key);
            if (count == 1)
                await _cacheService.SetExpiryAsync(key, ChallengeTtl);

            if (count > limit)
            {
                EmitThrottled(userId, dimension);
                throw new TooManyRequestException(message);
            }
        }

        private UnauthorizedException StepUpRejected(
            AuthSecurityMethod method,
            AuthSecurityReason reason,
            string message,
            int? userId = null)
        {
            EmitStepUpFailed(method, reason, userId);
            return new UnauthorizedException(message);
        }

        private void EmitStepUpFailed(AuthSecurityMethod method, AuthSecurityReason reason, int? userId) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.StepUpFailed,
                Outcome = AuthSecurityOutcome.Denied,
                Reason = reason,
                Method = method,
                Flow = AuthSecurityFlow.LoginStepUp,
                UserId = userId,
            });

        private void EmitThrottled(int userId, AuthThrottleDimension dimension) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.Throttled,
                Outcome = AuthSecurityOutcome.Throttled,
                Reason = AuthSecurityReason.LimitExceeded,
                Flow = AuthSecurityFlow.MfaDelivery,
                Dimension = dimension,
                UserId = userId,
            });

        private static void EnforceCooldown(PendingStepUpState state, string method, DateTime now)
        {
            var delivery = GetDeliveryState(state, method);
            if (delivery == null)
                return;

            if (delivery.SendCount >= MaxMethodSends)
                throw new TooManyRequestException($"Too many {method} verification deliveries have been requested for this sign-in.");

            if (delivery.CooldownEndsAtUtc.HasValue && delivery.CooldownEndsAtUtc.Value > now)
            {
                var seconds = Math.Max(1, (int)Math.Ceiling((delivery.CooldownEndsAtUtc.Value - now).TotalSeconds));
                throw new TooManyRequestException($"Please wait {seconds} seconds before requesting another {method} verification.");
            }
        }

        private static DeliveryState? GetDeliveryState(PendingStepUpState state, string method) =>
            method switch
            {
                SmsMethod => state.Sms,
                EmailMethod => state.EmailDelivery,
                _ => null,
            };

        private static void EnsureMethodAllowed(PendingStepUpState state, string method)
        {
            var availableMethods = GetAvailableMethods(state);
            if (!availableMethods.Contains(method, StringComparer.Ordinal))
                throw new BadRequestException("The requested sign-in verification method is unavailable.");
        }

        private async Task<PendingStepUpState?> GetStateByHashAsync(string challengeHash)
        {
            var json = await _cacheService.GetValueAsync(ChallengeKey(challengeHash));
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonConvert.DeserializeObject<PendingStepUpState>(json);
        }

        private async Task<bool> PersistStateAsync(
            PendingStepUpState state,
            string? previousChallengeHash = null,
            string? previousEmailTokenHash = null
        )
        {
            var ttl = state.ExpiresAtUtc - DateTime.UtcNow;
            if (ttl <= TimeSpan.Zero)
                return false;

            var json = JsonConvert.SerializeObject(state);
            if (!string.IsNullOrWhiteSpace(previousChallengeHash) && previousChallengeHash != state.ChallengeHash)
                await _cacheService.DeleteKeyAsync(ChallengeKey(previousChallengeHash));
            if (!string.IsNullOrWhiteSpace(previousEmailTokenHash) && previousEmailTokenHash != state.EmailTokenHash)
                await _cacheService.DeleteKeyAsync(EmailTokenKey(previousEmailTokenHash));

            var challengeStored = await _cacheService.SetValueAsync(ChallengeKey(state.ChallengeHash), json, ttl);
            if (!challengeStored)
                return false;

            if (!string.IsNullOrWhiteSpace(state.EmailTokenHash))
            {
                var emailStored = await _cacheService.SetValueAsync(EmailTokenKey(state.EmailTokenHash), json, ttl);
                if (!emailStored)
                {
                    await _cacheService.DeleteKeyAsync(ChallengeKey(state.ChallengeHash));
                    return false;
                }
            }

            return true;
        }

        private async Task DeleteStateAsync(PendingStepUpState state)
        {
            await _cacheService.DeleteKeyAsync(ChallengeKey(state.ChallengeHash));
            if (!string.IsNullOrWhiteSpace(state.EmailTokenHash))
                await _cacheService.DeleteKeyAsync(EmailTokenKey(state.EmailTokenHash));
        }

        private static LoginStepUpChallengeResponse ToChallengeResponse(string rawChallenge, PendingStepUpState state)
        {
            return new LoginStepUpChallengeResponse
            {
                Challenge = rawChallenge,
                ExpiresAtUtc = state.ExpiresAtUtc,
                AvailableMethods = GetAvailableMethods(state),
                MaskedPhone = CanUseSms(state) ? PhoneNumberFormatter.Mask(state.PhoneNumber!) : null,
                MaskedEmail = PhoneNumberFormatter.MaskEmail(state.Email)
            };
        }

        private static string[] GetAvailableMethods(PendingStepUpState state)
        {
            var methods = new List<string>();
            if (state.HasTotp)
                methods.Add(TotpMethod);
            if (CanUseSms(state))
                methods.Add(SmsMethod);
            if (state.HasRecoveryCodes)
                methods.Add(RecoveryCodeMethod);
            methods.Add(EmailMethod);
            return [.. methods];
        }

        private static bool CanUseSms(SmsMfaEnrollment? enrollment)
        {
            if (enrollment == null || !enrollment.IsSmsMfaEnabled || string.IsNullOrWhiteSpace(enrollment.PhoneNumber))
                return false;

            return EnvironmentSetting.AuthSmsMfaStepUpSmsEnabled &&
                (!string.IsNullOrWhiteSpace(EnvironmentSetting.TwilioAccountSid)
                    || EnvironmentSetting.AppEnvironment is "development" or "test" or "testing");
        }

        private static bool CanUseSms(PendingStepUpState state) => !string.IsNullOrWhiteSpace(state.PhoneNumber);

        private static string NormalizeMethod(string method)
        {
            var normalized = method?.Trim().ToLowerInvariant();
            return normalized switch
            {
                EmailMethod => EmailMethod,
                SmsMethod => SmsMethod,
                TotpMethod => TotpMethod,
                RecoveryCodeMethod => RecoveryCodeMethod,
                _ => throw new BadRequestException("Unsupported sign-in verification method.")
            };
        }

        private static string NormalizeReturnPath(string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl))
                return "/dashboard";

            var trimmed = returnUrl.Trim();
            if (!trimmed.StartsWith("/", StringComparison.Ordinal)
                || trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/\\", StringComparison.Ordinal))
                return "/dashboard";

            return trimmed;
        }

        private static string CreateRandomToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        private static string HashCode(string pendingId, string challengeHash, string code)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{pendingId}|{challengeHash}|{code}"));
            return Convert.ToHexString(bytes);
        }

        private static string ChallengeKey(string challengeHash) => $"auth:stepup:challenge:{challengeHash}";
        private static string EmailTokenKey(string emailTokenHash) => $"auth:stepup:email:{emailTokenHash}";
        private static string CompletionLockKey(string pendingId) => $"auth:stepup:lock:{pendingId}";
        private static string UserSendKey(int userId) => $"auth:stepup:send:user:{userId}";
        private static string IpSendKey(string ipAddress) => $"auth:stepup:send:ip:{ipAddress}";

        private sealed class PendingStepUpState
        {
            public required string PendingId
            {
                get; set;
            }
            public required string ChallengeHash
            {
                get; set;
            }
            /// <summary>
            /// The account's auth version when the challenge began. A challenge that outlives a
            /// security-sensitive change must not still be able to mint a session.
            /// </summary>
            public int AuthVersion
            {
                get; set;
            }
            public int UserId
            {
                get; set;
            }
            public required string Email
            {
                get; set;
            }
            public string? PhoneNumber
            {
                get; set;
            }
            public bool HasTotp
            {
                get; set;
            }
            public bool HasRecoveryCodes
            {
                get; set;
            }
            public SessionTransport Transport
            {
                get; set;
            }
            public bool RememberMe
            {
                get; set;
            }
            public required string TrustedDeviceId
            {
                get; set;
            }
            public required string DeviceType
            {
                get; set;
            }
            public required string ClientName
            {
                get; set;
            }
            public required string IpAddress
            {
                get; set;
            }
            public required string ReturnPath
            {
                get; set;
            }
            public DateTime ExpiresAtUtc
            {
                get; set;
            }
            public string? SmsCodeHash
            {
                get; set;
            }
            public string? EmailTokenHash
            {
                get; set;
            }
            public int SmsFailedAttempts
            {
                get; set;
            }
            public int TotpFailedAttempts
            {
                get; set;
            }
            public int RecoveryFailedAttempts
            {
                get; set;
            }
            public required DeliveryState Sms
            {
                get; set;
            }
            public required DeliveryState EmailDelivery
            {
                get; set;
            }
        }

        private sealed class DeliveryState
        {
            public int SendCount
            {
                get; set;
            }
            public DateTime? CooldownEndsAtUtc
            {
                get; set;
            }
        }
    }
}
