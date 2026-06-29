using System.Security.Cryptography;
using System.Text;

using backend.main.application.environment;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.notifications;
using backend.main.features.cache;
using backend.main.shared.exceptions.http;
using backend.main.shared.utilities;
using backend.main.shared.utilities.logger;

using Microsoft.IdentityModel.Tokens;

using Newtonsoft.Json;

namespace backend.main.features.auth.mfa
{
    public sealed class MfaEnrollmentService : IMfaEnrollmentService
    {
        private static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan StartRateLimitTtl = TimeSpan.FromMinutes(15);
        private const int MaxOtpAttempts = 5;
        private const int MaxStartRequests = 5;
        private const string SmsChannel = "sms";
        private readonly IMfaEnrollmentRepository _repository;
        private readonly ICacheService _cacheService;
        private readonly IAuthNotificationService _notificationService;
        private readonly string _proofSecret;

        public MfaEnrollmentService(
            IMfaEnrollmentRepository repository,
            ICacheService cacheService,
            IAuthNotificationService notificationService
        )
        {
            _repository = repository;
            _cacheService = cacheService;
            _notificationService = notificationService;
            _proofSecret = EnvironmentSetting.JwtSecretKeyVerification;
        }

        public async Task<MfaChallengeResponse> StartEnrollmentAsync(int userId, string phoneNumber)
        {
            try
            {
                EnsureEnrollmentAvailable();
                var normalizedPhone = PhoneNumberFormatter.Normalize(phoneNumber);
                return await StartChallengeAsync(userId, normalizedPhone, "mfa enrollment");
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[MfaEnrollmentService] StartEnrollmentAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<MfaChallengeResponse> StartEnableAsync(int userId)
        {
            try
            {
                var enrollment = await _repository.GetByUserIdAsync(userId);
                if (enrollment == null || string.IsNullOrWhiteSpace(enrollment.PhoneNumber))
                    throw new ConflictException("SMS MFA is not configured for this account.");

                if (enrollment.IsSmsMfaEnabled)
                    throw new ConflictException("SMS MFA is already enabled for this account.");

                return await StartChallengeAsync(userId, enrollment.PhoneNumber, "mfa re-enable");
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[MfaEnrollmentService] StartEnableAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<SmsMfaEnrollment> VerifyEnrollmentAsync(int userId, string code, string challenge)
        {
            SmsMfaEnrollment? existingEnrollment = null;
            EnrollmentChallengeState? state = null;

            try
            {
                state = await GetStateByChallengeAsync(challenge);
                if (state == null || state.UserId != userId || state.Challenge != challenge)
                    throw new UnauthorizedException("Invalid or expired MFA enrollment challenge.");

                var expectedProof = ComputeProof(
                    state.UserId,
                    state.PhoneNumber,
                    state.ExpiresAtUtc,
                    challenge,
                    code
                );

                if (!CryptoHelper.FixedTimeEquals(state.Proof, expectedProof))
                {
                    var attempts = await RecordFailedAttemptAsync(challenge);
                    if (attempts >= MaxOtpAttempts)
                        await DeleteStateAsync(state);

                    throw new UnauthorizedException("Invalid or expired MFA enrollment code.");
                }

                existingEnrollment = await _repository.GetByUserIdAsync(userId);
                var isReEnable = IsReEnable(existingEnrollment, state.PhoneNumber);
                if (!isReEnable)
                    EnsureEnrollmentAvailable();

                var verifiedAtUtc = isReEnable
                    ? existingEnrollment!.PhoneVerifiedAtUtc ?? DateTime.UtcNow
                    : DateTime.UtcNow;
                var enrollment = await _repository.UpsertVerifiedPhoneAsync(
                    userId,
                    state.PhoneNumber,
                    verifiedAtUtc
                );

                await DeleteStateAsync(state);
                var action = isReEnable ? "re-enable" : "enroll";
                LogAudit(userId, action, true, "sms configuration verified");
                return enrollment;
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                {
                    var action = IsReEnable(existingEnrollment, state?.PhoneNumber) ? "re-enable" : "enroll";
                    LogAudit(userId, action, false, ex.Message);
                    throw;
                }

                Logger.Error($"[MfaEnrollmentService] VerifyEnrollmentAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<SmsMfaEnrollment?> DisableAsync(int userId)
        {
            try
            {
                var enrollment = await _repository.GetByUserIdAsync(userId);
                if (enrollment == null || string.IsNullOrWhiteSpace(enrollment.PhoneNumber))
                    throw new ConflictException("SMS MFA is not configured for this account.");

                if (!enrollment.IsSmsMfaEnabled)
                    throw new ConflictException("SMS MFA is already disabled for this account.");

                var updated = await _repository.SetEnabledAsync(userId, false)
                    ?? throw new ConflictException("SMS MFA is not configured for this account.");

                LogAudit(userId, "disable", true, "sms configuration disabled");
                return updated;
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                {
                    LogAudit(userId, "disable", false, ex.Message);
                    throw;
                }

                Logger.Error($"[MfaEnrollmentService] DisableAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task RemoveAsync(int userId)
        {
            try
            {
                var enrollment = await _repository.GetByUserIdAsync(userId);
                if (enrollment == null || string.IsNullOrWhiteSpace(enrollment.PhoneNumber))
                    throw new ConflictException("SMS MFA is not configured for this account.");

                if (!await _repository.RemoveAsync(userId))
                    throw new ConflictException("SMS MFA is not configured for this account.");

                LogAudit(userId, "remove", true, "sms configuration removed");
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                {
                    LogAudit(userId, "remove", false, ex.Message);
                    throw;
                }

                Logger.Error($"[MfaEnrollmentService] RemoveAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        private async Task<MfaChallengeResponse> StartChallengeAsync(int userId, string phoneNumber, string purpose)
        {
            await EnforceStartRateLimitAsync(userId);

            var existingState = await GetStateByUserAsync(userId);
            if (existingState != null)
                await DeleteStateAsync(existingState);

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var challenge = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
            var expiresAtUtc = DateTime.UtcNow.Add(ChallengeTtl);
            var state = new EnrollmentChallengeState
            {
                UserId = userId,
                PhoneNumber = phoneNumber,
                Challenge = challenge,
                Proof = ComputeProof(userId, phoneNumber, expiresAtUtc, challenge, code),
                ExpiresAtUtc = expiresAtUtc,
            };

            if (!await PersistStateAsync(state))
                throw new NotAvailableException();

            try
            {
                await _notificationService.SendSmsMfaAsync(
                    phoneNumber,
                    code,
                    challenge,
                    expiresAtUtc,
                    purpose
                );
            }
            catch
            {
                await DeleteStateAsync(state);
                throw;
            }

            return new MfaChallengeResponse
            {
                Challenge = challenge,
                ExpiresAtUtc = expiresAtUtc,
                Channel = SmsChannel,
                MaskedDestination = PhoneNumberFormatter.Mask(phoneNumber),
            };
        }

        private async Task EnforceStartRateLimitAsync(int userId)
        {
            var key = StartKey(userId);
            var count = await _cacheService.IncrementAsync(key);
            if (count == 1)
                await _cacheService.SetExpiryAsync(key, StartRateLimitTtl);

            if (count > MaxStartRequests)
                throw new TooManyRequestException("Too many SMS MFA verification codes have been requested. Please try again later.");
        }

        private async Task<bool> PersistStateAsync(EnrollmentChallengeState state)
        {
            var json = JsonConvert.SerializeObject(state);
            var stateStored = await _cacheService.SetValueAsync(UserKey(state.UserId), json, ChallengeTtl);
            var challengeStored = await _cacheService.SetValueAsync(
                ChallengeKey(state.Challenge),
                json,
                ChallengeTtl
            );

            if (stateStored && challengeStored)
                return true;

            _ = await _cacheService.DeleteKeyAsync(UserKey(state.UserId));
            _ = await _cacheService.DeleteKeyAsync(ChallengeKey(state.Challenge));
            return false;
        }

        private async Task<EnrollmentChallengeState?> GetStateByUserAsync(int userId)
        {
            var json = await _cacheService.GetValueAsync(UserKey(userId));
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonConvert.DeserializeObject<EnrollmentChallengeState>(json);
        }

        private async Task<EnrollmentChallengeState?> GetStateByChallengeAsync(string challenge)
        {
            var json = await _cacheService.GetValueAsync(ChallengeKey(challenge));
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonConvert.DeserializeObject<EnrollmentChallengeState>(json);
        }

        private async Task DeleteStateAsync(EnrollmentChallengeState state)
        {
            _ = await _cacheService.DeleteKeyAsync(UserKey(state.UserId));
            _ = await _cacheService.DeleteKeyAsync(ChallengeKey(state.Challenge));
            _ = await _cacheService.DeleteKeyAsync(AttemptKey(state.Challenge));
        }

        private async Task<long> RecordFailedAttemptAsync(string challenge)
        {
            var key = AttemptKey(challenge);
            var attempts = await _cacheService.IncrementAsync(key);
            _ = await _cacheService.SetExpiryAsync(key, ChallengeTtl);
            return attempts;
        }

        private string ComputeProof(
            int userId,
            string phoneNumber,
            DateTime expiresAtUtc,
            string challenge,
            string code
        )
        {
            var material = string.Join(
                "|",
                userId,
                phoneNumber,
                expiresAtUtc.ToUniversalTime().Ticks,
                challenge,
                code
            );

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_proofSecret));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(material)));
        }

        private static bool IsReEnable(SmsMfaEnrollment? enrollment, string? pendingPhoneNumber)
        {
            return enrollment != null
                && !string.IsNullOrWhiteSpace(enrollment.PhoneNumber)
                && string.Equals(enrollment.PhoneNumber, pendingPhoneNumber, StringComparison.Ordinal)
                && !enrollment.IsSmsMfaEnabled;
        }

        private static void LogAudit(int userId, string action, bool success, string reason)
        {
            Logger.Info($"[AuthMfaAudit] userId={userId} method=sms action={action} success={success} reason={reason}");
        }

        private static string UserKey(int userId) => $"mfa:enrollment:user:{userId}";
        private static string ChallengeKey(string challenge) => $"mfa:enrollment:challenge:{challenge}";
        private static string AttemptKey(string challenge) => $"mfa:enrollment:attempt:{challenge}";
        private static string StartKey(int userId) => $"mfa:enrollment:start:user:{userId}";

        private static void EnsureEnrollmentAvailable()
        {
            if (!EnvironmentSetting.AuthSmsMfaEnrollmentEnabled)
                throw new NotAvailableException("SMS MFA enrollment is currently unavailable.");
        }

        private sealed class EnrollmentChallengeState
        {
            public int UserId
            {
                get; set;
            }

            public required string PhoneNumber
            {
                get; set;
            }

            public required string Challenge
            {
                get; set;
            }

            public required string Proof
            {
                get; set;
            }

            public DateTime ExpiresAtUtc
            {
                get; set;
            }
        }
    }
}
