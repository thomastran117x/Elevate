using System.Security.Cryptography;
using System.Text;

using backend.main.application.environment;
using backend.main.features.auth;
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
        private const int MaxOtpAttempts = 5;
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

        public async Task<MfaStatusResponse> GetStatusAsync(int userId)
        {
            try
            {
                var enrollment = await _repository.GetByUserIdAsync(userId);
                return ToStatusResponse(enrollment, EnvironmentSetting.AuthSmsMfaEnrollmentEnabled);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[MfaEnrollmentService] GetStatusAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<MfaChallengeResponse> StartEnrollmentAsync(int userId, string phoneNumber)
        {
            try
            {
                EnsureEnrollmentAvailable();

                var normalizedPhone = PhoneNumberFormatter.Normalize(phoneNumber);
                var existingState = await GetStateByUserAsync(userId);
                if (existingState != null)
                    await DeleteStateAsync(existingState);

                var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
                var challenge = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
                var expiresAtUtc = DateTime.UtcNow.Add(ChallengeTtl);
                var state = new EnrollmentChallengeState
                {
                    UserId = userId,
                    PhoneNumber = normalizedPhone,
                    Challenge = challenge,
                    Proof = ComputeProof(userId, normalizedPhone, expiresAtUtc, challenge, code),
                    ExpiresAtUtc = expiresAtUtc,
                };

                if (!await PersistStateAsync(state))
                    throw new NotAvailableException();

                try
                {
                    await _notificationService.SendSmsMfaAsync(
                        normalizedPhone,
                        code,
                        challenge,
                        expiresAtUtc,
                        "mfa enrollment"
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
                    MaskedDestination = PhoneNumberFormatter.Mask(normalizedPhone),
                };
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[MfaEnrollmentService] StartEnrollmentAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<MfaStatusResponse> VerifyEnrollmentAsync(int userId, string code, string challenge)
        {
            try
            {
                EnsureEnrollmentAvailable();

                var state = await GetStateByChallengeAsync(challenge);
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

                var enrollment = await _repository.UpsertVerifiedPhoneAsync(
                    userId,
                    state.PhoneNumber,
                    DateTime.UtcNow
                );

                await DeleteStateAsync(state);

                return ToStatusResponse(enrollment, EnvironmentSetting.AuthSmsMfaEnrollmentEnabled);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[MfaEnrollmentService] VerifyEnrollmentAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<MfaStatusResponse> DisableAsync(int userId)
        {
            try
            {
                EnsureEnrollmentAvailable();

                var enrollment = await _repository.SetEnabledAsync(userId, false)
                    ?? await _repository.GetByUserIdAsync(userId);

                return ToStatusResponse(enrollment, EnvironmentSetting.AuthSmsMfaEnrollmentEnabled);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[MfaEnrollmentService] DisableAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        private static MfaStatusResponse ToStatusResponse(
            SmsMfaEnrollment? enrollment,
            bool enrollmentAvailable
        )
        {
            return new MfaStatusResponse
            {
                SmsEnrollmentAvailable = enrollmentAvailable,
                IsSmsMfaEnabled = enrollment?.IsSmsMfaEnabled ?? false,
                MaskedPhoneNumber = string.IsNullOrWhiteSpace(enrollment?.PhoneNumber)
                    ? null
                    : PhoneNumberFormatter.Mask(enrollment.PhoneNumber),
                PhoneVerifiedAtUtc = enrollment?.PhoneVerifiedAtUtc,
            };
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

        private static string UserKey(int userId) => $"mfa:enrollment:user:{userId}";
        private static string ChallengeKey(string challenge) => $"mfa:enrollment:challenge:{challenge}";
        private static string AttemptKey(string challenge) => $"mfa:enrollment:attempt:{challenge}";

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
