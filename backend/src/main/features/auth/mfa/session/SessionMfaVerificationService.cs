using System.Security.Cryptography;
using System.Text;

using backend.main.application.environment;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa.totp;
using backend.main.features.auth.notifications;
using backend.main.features.auth.token;
using backend.main.features.cache;
using backend.main.shared.exceptions.http;
using backend.main.shared.utilities;
using backend.main.shared.utilities.logger;

using Microsoft.Extensions.Options;

using Newtonsoft.Json;

using StackExchange.Redis;

namespace backend.main.features.auth.mfa.session
{
    /// <summary>
    /// Records a short-lived <see cref="StepUpProof"/> for the session after the user proves a
    /// second factor (TOTP, SMS, or email). Email is the universal fallback and always
    /// available, so any authenticated user can satisfy a <c>[RequireMfa]</c> gate.
    /// </summary>
    public sealed class SessionMfaVerificationService : ISessionMfaVerificationService
    {
        private static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan StartRateLimitTtl = TimeSpan.FromMinutes(15);
        private const int MaxOtpAttempts = 5;
        private const int MaxStartRequests = 6;

        private const string TotpMethod = "totp";
        private const string SmsMethod = "sms";
        private const string EmailMethod = "email";

        // Returns the session's proof and the user's step-up generation in one round trip.
        private const string ReadProofScript = @"
return { redis.call('GET', KEYS[1]) or false, redis.call('GET', KEYS[2]) or false }";

        private readonly ICacheService _cacheService;
        private readonly IAuthNotificationService _notificationService;
        private readonly IMfaEnrollmentRepository _smsEnrollmentRepository;
        private readonly ITotpMfaEnrollmentService _totpMfaEnrollmentService;
        private readonly ITokenService _tokenService;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _proofLifetime;
        private readonly string _proofSecret;

        public SessionMfaVerificationService(
            ICacheService cacheService,
            IAuthNotificationService notificationService,
            IMfaEnrollmentRepository smsEnrollmentRepository,
            ITotpMfaEnrollmentService totpMfaEnrollmentService,
            ITokenService tokenService,
            TimeProvider timeProvider,
            IOptions<StepUpOptions> stepUpOptions
        )
        {
            _cacheService = cacheService;
            _notificationService = notificationService;
            _smsEnrollmentRepository = smsEnrollmentRepository;
            _totpMfaEnrollmentService = totpMfaEnrollmentService;
            _tokenService = tokenService;
            _timeProvider = timeProvider;
            _proofLifetime = stepUpOptions.Value.ProofLifetime;
            _proofSecret = EnvironmentSetting.JwtSecretKeyVerification;
        }

        public async Task<SessionMfaOptionsResponse> GetOptionsAsync(int userId, string email)
        {
            try
            {
                var methods = await GetAvailableMethodsAsync(userId);
                var smsEnrollment = await _smsEnrollmentRepository.GetByUserIdAsync(userId);

                return new SessionMfaOptionsResponse
                {
                    AvailableMethods = methods,
                    MaskedPhone = CanUseSms(smsEnrollment)
                        ? PhoneNumberFormatter.Mask(smsEnrollment!.PhoneNumber)
                        : null,
                    MaskedEmail = PhoneNumberFormatter.MaskEmail(email),
                };
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[SessionMfaVerificationService] GetOptionsAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<SessionMfaStartResponse> StartAsync(int userId, string email, string method)
        {
            try
            {
                var normalizedMethod = NormalizeMethod(method);
                await EnsureMethodAllowedAsync(userId, normalizedMethod);

                var now = DateTime.UtcNow;
                var expiresAtUtc = now.Add(ChallengeTtl);

                // TOTP is stateless — the code is validated live against the enrollment,
                // so there is nothing to deliver or persist here.
                if (normalizedMethod == TotpMethod)
                {
                    return new SessionMfaStartResponse
                    {
                        SelectedMethod = TotpMethod,
                        MaskedDestination = "authenticator app",
                        ExpiresAtUtc = expiresAtUtc,
                        CooldownEndsAtUtc = DateTime.MinValue,
                    };
                }

                await EnforceStartRateLimitAsync(userId);

                var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
                string maskedDestination;

                if (normalizedMethod == SmsMethod)
                {
                    var smsEnrollment = await _smsEnrollmentRepository.GetByUserIdAsync(userId);
                    if (!CanUseSms(smsEnrollment))
                        throw new BadRequestException("SMS verification is unavailable for this account.");

                    maskedDestination = PhoneNumberFormatter.Mask(smsEnrollment!.PhoneNumber);
                }
                else
                {
                    maskedDestination = PhoneNumberFormatter.MaskEmail(email);
                }

                var state = new PendingState
                {
                    UserId = userId,
                    Method = normalizedMethod,
                    CodeHash = ComputeProof(userId, normalizedMethod, expiresAtUtc, code),
                    ExpiresAtUtc = expiresAtUtc,
                    CooldownEndsAtUtc = now.Add(ResendCooldown),
                };

                if (!await PersistStateAsync(state))
                    throw new NotAvailableException();

                try
                {
                    if (normalizedMethod == SmsMethod)
                    {
                        var smsEnrollment = await _smsEnrollmentRepository.GetByUserIdAsync(userId);
                        await _notificationService.SendSmsMfaAsync(
                            smsEnrollment!.PhoneNumber,
                            code,
                            challenge: string.Empty,
                            expiresAtUtc,
                            "security verification"
                        );
                    }
                    else
                    {
                        await _notificationService.SendEmailMfaCodeAsync(email, code);
                    }
                }
                catch
                {
                    await DeleteStateAsync(userId);
                    throw;
                }

                return new SessionMfaStartResponse
                {
                    SelectedMethod = normalizedMethod,
                    MaskedDestination = maskedDestination,
                    ExpiresAtUtc = expiresAtUtc,
                    CooldownEndsAtUtc = state.CooldownEndsAtUtc ?? now.Add(ResendCooldown),
                };
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[SessionMfaVerificationService] StartAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task VerifyAsync(
            int userId,
            string email,
            string sessionId,
            int authVersion,
            string method,
            string code
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                    throw new UnauthorizedException("This session cannot be verified. Please sign in again.");

                var normalizedMethod = NormalizeMethod(method);

                // Captured before the code is checked, so a factor change that completes while this
                // verification is in flight makes the proof write fail instead of outliving it.
                var generation = await GetGenerationAsync(userId);
                var factorState = await GetFactorStateAsync(userId);

                if (normalizedMethod == TotpMethod)
                {
                    try
                    {
                        // Throws UnauthorizedException on an invalid code.
                        await _totpMfaEnrollmentService.VerifyPersistedCodeAsync(userId, code);
                    }
                    catch (UnauthorizedException)
                    {
                        // Throttle brute-force guessing against the gate: TOTP verification is
                        // otherwise stateless, so cap failures per user over a short window.
                        var totpAttempts = await _cacheService.IncrementAsync(TotpAttemptKey(userId));
                        if (totpAttempts == 1)
                            await _cacheService.SetExpiryAsync(TotpAttemptKey(userId), ChallengeTtl);
                        if (totpAttempts >= MaxOtpAttempts)
                            throw new TooManyRequestException("Too many verification attempts. Please try again later.");

                        LogAudit(userId, normalizedMethod, false);
                        throw;
                    }

                    await _cacheService.DeleteKeyAsync(TotpAttemptKey(userId));
                    await RecordProofAsync(userId, sessionId, authVersion, normalizedMethod, generation, factorState);
                    LogAudit(userId, normalizedMethod, true);
                    return;
                }

                var state = await GetStateAsync(userId);
                if (state == null || state.Method != normalizedMethod)
                    throw new UnauthorizedException("Invalid or expired verification challenge.");

                if (state.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    await DeleteStateAsync(userId);
                    throw new UnauthorizedException("Invalid or expired verification challenge.");
                }

                var expected = ComputeProof(userId, normalizedMethod, state.ExpiresAtUtc, code);
                if (!CryptoHelper.FixedTimeEquals(state.CodeHash, expected))
                {
                    state.FailedAttempts += 1;
                    if (state.FailedAttempts >= MaxOtpAttempts)
                        await DeleteStateAsync(userId);
                    else
                        await PersistStateAsync(state);

                    LogAudit(userId, normalizedMethod, false);
                    throw new UnauthorizedException("Invalid or expired verification code.");
                }

                await DeleteStateAsync(userId);
                await RecordProofAsync(userId, sessionId, authVersion, normalizedMethod, generation, factorState);
                LogAudit(userId, normalizedMethod, true);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[SessionMfaVerificationService] VerifyAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<bool> HasFreshProofAsync(int userId, string? sessionId, int authVersion)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return false;

            // Read the proof and the generation together, and refuse when the cache cannot answer:
            // an unavailable generation must never be mistaken for generation 0.
            var read = await _cacheService.TryEvalAsync(
                ReadProofScript,
                [StepUpProofKeys.ForSession(sessionId), StepUpProofKeys.GenerationForUser(userId)],
                []
            );
            if (!read.Succeeded || read.Value is null)
                return false;

            var (json, currentGenerationValue) = ReadPair(read.Value);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            StepUpProof? proof;
            try
            {
                proof = JsonConvert.DeserializeObject<StepUpProof>(json);
            }
            catch (JsonException)
            {
                proof = null;
            }

            if (proof == null || proof.UserId != userId || proof.SessionId != sessionId)
                return false;

            // A completed factor change retires every earlier proof, on every session.
            var currentGeneration = long.TryParse(currentGenerationValue, out var parsedGeneration)
                ? parsedGeneration
                : 0;
            if (proof.Generation != currentGeneration)
                return false;

            // A newer auth version means credentials rotated after this proof was made; it can
            // never become valid again, so drop it rather than leave it for the TTL.
            if (proof.AuthVersion != authVersion)
            {
                await ClearSessionProofAsync(sessionId);
                return false;
            }

            // Redis expiry is only a backstop: the window is judged from the recorded
            // verification time, so a lingering key or clock drift cannot stretch it.
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var verifiedAtUtc = DateTime.SpecifyKind(proof.VerifiedAtUtc, DateTimeKind.Utc);
            if (verifiedAtUtc > now || now >= verifiedAtUtc.Add(_proofLifetime))
                return false;

            // The database is authoritative: a factor change committed after this proof was made
            // retires it even if the cache-side generation was never advanced.
            return string.Equals(proof.FactorState, await GetFactorStateAsync(userId), StringComparison.Ordinal);
        }

        public async Task ClearSessionProofAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            _ = await _cacheService.DeleteKeyAsync(StepUpProofKeys.ForSession(sessionId));
        }

        public async Task ClearUserProofsAsync(int userId)
        {
            // One atomic counter bump retires proofs on every session, including any a concurrent
            // verify is about to write. INCR never returns 0, so 0 means the cache was unreachable.
            var generation = await _cacheService.IncrementAsync(StepUpProofKeys.GenerationForUser(userId));
            if (generation <= 0)
            {
                Logger.Error($"[SessionMfaVerificationService] Could not retire step-up proofs for userId={userId}.");
                throw new NotAvailableException();
            }
        }

        private async Task<string> GetFactorStateAsync(int userId) =>
            StepUpFactorState.From(
                await _smsEnrollmentRepository.GetByUserIdAsync(userId),
                await _totpMfaEnrollmentService.GetEnrollmentAsync(userId)
            );

        private static (string? Proof, string? Generation) ReadPair(object value)
        {
            if (value is RedisResult result)
            {
                var parts = (RedisResult[]?)result ?? [];
                return (ReadPart(parts, 0), ReadPart(parts, 1));
            }

            if (value is object?[] items)
                return (items.ElementAtOrDefault(0)?.ToString(), items.ElementAtOrDefault(1)?.ToString());

            return (null, null);
        }

        private static string? ReadPart(RedisResult[] parts, int index) =>
            parts.Length > index && !parts[index].IsNull ? parts[index].ToString() : null;

        private async Task<long> GetGenerationAsync(int userId)
        {
            var value = await _cacheService.GetValueAsync(StepUpProofKeys.GenerationForUser(userId));
            return long.TryParse(value, out var generation) ? generation : 0;
        }

        private async Task RecordProofAsync(
            int userId,
            string sessionId,
            int authVersion,
            string method,
            long generation,
            string factorState
        )
        {
            var proof = new StepUpProof
            {
                UserId = userId,
                SessionId = sessionId,
                AuthVersion = authVersion,
                Method = method,
                VerifiedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                Generation = generation,
                FactorState = factorState,
            };

            // Written once with an absolute TTL; nothing re-sets it, so the window never slides.
            // The write is conditional on the refresh session still existing, so a verify that
            // races logout cannot leave a proof behind for a logged-out access token.
            var result = await _tokenService.StoreStepUpProofAsync(
                sessionId,
                userId,
                generation,
                JsonConvert.SerializeObject(proof),
                _proofLifetime
            );

            switch (result)
            {
                case StepUpProofWriteResult.Stored:
                    return;
                case StepUpProofWriteResult.SessionEnded:
                    throw new UnauthorizedException("This session has ended. Please sign in again.");
                case StepUpProofWriteResult.Superseded:
                    throw new ConflictException("Your security settings changed during verification. Please verify again.");
                default:
                    // Reporting success without a stored proof would loop the user through verification.
                    throw new NotAvailableException();
            }
        }

        private async Task<string[]> GetAvailableMethodsAsync(int userId)
        {
            var methods = new List<string>();

            var totpEnrollment = await _totpMfaEnrollmentService.GetEnrollmentAsync(userId);
            if (totpEnrollment?.IsTotpMfaEnabled == true && EnvironmentSetting.AuthTotpMfaStepUpEnabled)
                methods.Add(TotpMethod);

            var smsEnrollment = await _smsEnrollmentRepository.GetByUserIdAsync(userId);
            if (CanUseSms(smsEnrollment))
                methods.Add(SmsMethod);

            // Email always works and requires no prior enrollment — the universal fallback.
            methods.Add(EmailMethod);
            return [.. methods];
        }

        private async Task EnsureMethodAllowedAsync(int userId, string method)
        {
            var methods = await GetAvailableMethodsAsync(userId);
            if (!methods.Contains(method, StringComparer.Ordinal))
                throw new BadRequestException("The requested verification method is unavailable.");
        }

        private static bool CanUseSms(SmsMfaEnrollment? enrollment)
        {
            if (enrollment == null || !enrollment.IsSmsMfaEnabled || string.IsNullOrWhiteSpace(enrollment.PhoneNumber))
                return false;

            return EnvironmentSetting.AuthSmsMfaStepUpSmsEnabled
                && (!string.IsNullOrWhiteSpace(EnvironmentSetting.TwilioAccountSid)
                    || EnvironmentSetting.AppEnvironment is "development" or "test" or "testing");
        }

        private async Task EnforceStartRateLimitAsync(int userId)
        {
            var key = StartKey(userId);
            var count = await _cacheService.IncrementAsync(key);
            if (count == 1)
                await _cacheService.SetExpiryAsync(key, StartRateLimitTtl);

            if (count > MaxStartRequests)
                throw new TooManyRequestException("Too many verification codes have been requested. Please try again later.");
        }

        private async Task<bool> PersistStateAsync(PendingState state)
        {
            // Preserve the original expiry so a failed attempt near the end of the
            // window cannot extend the challenge's advertised lifetime.
            var ttl = state.ExpiresAtUtc - DateTime.UtcNow;
            if (ttl <= TimeSpan.Zero)
                return false;

            var json = JsonConvert.SerializeObject(state);
            return await _cacheService.SetValueAsync(UserKey(state.UserId), json, ttl);
        }

        private async Task<PendingState?> GetStateAsync(int userId)
        {
            var json = await _cacheService.GetValueAsync(UserKey(userId));
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonConvert.DeserializeObject<PendingState>(json);
        }

        private async Task DeleteStateAsync(int userId)
        {
            _ = await _cacheService.DeleteKeyAsync(UserKey(userId));
        }

        private string ComputeProof(int userId, string method, DateTime expiresAtUtc, string code)
        {
            var material = string.Join(
                "|",
                userId,
                method,
                expiresAtUtc.ToUniversalTime().Ticks,
                code
            );

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_proofSecret));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(material)));
        }

        private static string NormalizeMethod(string method)
        {
            var normalized = method?.Trim().ToLowerInvariant();
            return normalized switch
            {
                TotpMethod => TotpMethod,
                SmsMethod => SmsMethod,
                EmailMethod => EmailMethod,
                _ => throw new BadRequestException("Unsupported verification method.")
            };
        }

        private static void LogAudit(int userId, string method, bool success)
        {
            Logger.Info($"[SessionMfaAudit] userId={userId} method={method} success={success}");
        }

        private static string UserKey(int userId) => $"mfa:stepup:user:{userId}";
        private static string StartKey(int userId) => $"mfa:stepup:start:user:{userId}";
        private static string TotpAttemptKey(int userId) => $"mfa:stepup:totp-attempts:user:{userId}";

        private sealed class PendingState
        {
            public int UserId
            {
                get; set;
            }
            public required string Method
            {
                get; set;
            }
            public required string CodeHash
            {
                get; set;
            }
            public DateTime ExpiresAtUtc
            {
                get; set;
            }
            public int FailedAttempts
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
