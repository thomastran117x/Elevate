using System.Security.Cryptography;

using backend.main.application.environment;
using backend.main.features.auth.contracts.responses;
using backend.main.features.cache;
using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

using Newtonsoft.Json;

using OtpNet;

using StackExchange.Redis;

namespace backend.main.features.auth.mfa.totp
{
    public sealed class TotpMfaEnrollmentService : ITotpMfaEnrollmentService
    {
        private const string ReplayGuardClaimLua = @"
            local key = KEYS[1]
            local matchedWindow = tonumber(ARGV[1])
            local ttlMs = tonumber(ARGV[2])

            local stored = redis.call('GET', key)
            if stored then
                local lastWindow = tonumber(stored)
                if lastWindow and matchedWindow <= lastWindow then
                    return 0
                end
            end

            redis.call('SET', key, tostring(matchedWindow), 'PX', ttlMs)
            return 1
            ";
        private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan VerifyLockTtl = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ReplayGuardTtl = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan ActionAttemptTtl = TimeSpan.FromMinutes(10);
        private const int MaxFailedAttempts = 5;
        private const int MaxActionAttempts = 5;
        private const int SecretSizeBytes = 20;
        private const int TagSizeBytes = 16;
        private const int NonceSizeBytes = 12;
        private const int EncryptionKeyVersion = 1;
        private const string IssuerName = "EventXperience";

        private readonly ITotpMfaEnrollmentRepository _repository;
        private readonly ICacheService _cacheService;

        public TotpMfaEnrollmentService(
            ITotpMfaEnrollmentRepository repository,
            ICacheService cacheService
        )
        {
            _repository = repository;
            _cacheService = cacheService;
        }

        public async Task<TotpMfaEnrollment?> GetEnrollmentAsync(int userId)
        {
            try
            {
                return await _repository.GetByUserIdAsync(userId);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[TotpMfaEnrollmentService] GetEnrollmentAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<TotpEnrollmentStartResponse> StartEnrollmentAsync(int userId, string email)
        {
            try
            {
                EnsureEnrollmentAvailable();

                var existing = await _repository.GetByUserIdAsync(userId);
                if (existing != null && !string.IsNullOrWhiteSpace(existing.EncryptedSecret))
                    throw new ConflictException("TOTP MFA is already configured for this account.");

                var secretBytes = KeyGeneration.GenerateRandomKey(SecretSizeBytes);
                var secretBase32 = Base32Encoding.ToString(secretBytes);
                var expiresAtUtc = DateTime.UtcNow.Add(PendingTtl);

                var state = new PendingEnrollmentState
                {
                    SecretBase32 = secretBase32,
                    FailedAttempts = 0,
                };

                var pendingStored = await _cacheService.SetValueAsync(
                    PendingKey(userId),
                    JsonConvert.SerializeObject(state),
                    PendingTtl
                );
                if (!pendingStored)
                    throw new NotAvailableException();

                var qrCodeUri = BuildQrCodeUri(secretBase32, email);

                return new TotpEnrollmentStartResponse
                {
                    SecretKey = secretBase32,
                    QrCodeUri = qrCodeUri,
                    ExpiresAtUtc = expiresAtUtc,
                };
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[TotpMfaEnrollmentService] StartEnrollmentAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<TotpMfaEnrollment> VerifyEnrollmentAsync(int userId, string code)
        {
            try
            {
                EnsureEnrollmentAvailable();

                var lockValue = Guid.NewGuid().ToString("N");
                var lockKey = VerifyLockKey(userId);
                if (!await _cacheService.AcquireLockAsync(lockKey, lockValue, VerifyLockTtl))
                    throw new TooManyRequestException("Enrollment verification is already in progress. Please try again shortly.");

                try
                {
                    var json = await _cacheService.GetValueAsync(PendingKey(userId));
                    if (string.IsNullOrWhiteSpace(json))
                        throw new UnauthorizedException("No pending TOTP enrollment found. Please restart the enrollment process.");

                    var state = JsonConvert.DeserializeObject<PendingEnrollmentState>(json)!;

                    ValidateCodeFormat(code);

                    var secretBytes = Base32Encoding.ToBytes(state.SecretBase32);
                    var totp = new Totp(secretBytes);

                    if (!totp.VerifyTotp(DateTime.UtcNow, code, out var matchedWindow, VerificationWindow.RfcSpecifiedNetworkDelay))
                    {
                        state.FailedAttempts += 1;
                        if (state.FailedAttempts >= MaxFailedAttempts)
                        {
                            await _cacheService.DeleteKeyAsync(PendingKey(userId));
                            throw new UnauthorizedException("Too many failed attempts. Please restart the enrollment process.");
                        }

                        await _cacheService.SetValueAsync(PendingKey(userId), JsonConvert.SerializeObject(state), PendingTtl);
                        throw new UnauthorizedException("Invalid TOTP code. Please check your authenticator app and try again.");
                    }

                    await CheckAndUpdateReplayGuardAsync(userId, matchedWindow, false);

                    var encryptedSecret = Encrypt(secretBytes);
                    var enrolledAtUtc = DateTime.UtcNow;
                    var enrollment = await _repository.UpsertAsync(userId, encryptedSecret, EncryptionKeyVersion, enrolledAtUtc);

                    await _cacheService.DeleteKeyAsync(PendingKey(userId));
                    LogAudit(userId, "enroll", true, "totp configuration verified");
                    return enrollment;
                }
                finally
                {
                    await _cacheService.ReleaseLockAsync(lockKey, lockValue);
                }
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                {
                    LogAudit(userId, "enroll", false, ex.Message);
                    throw;
                }

                Logger.Error($"[TotpMfaEnrollmentService] VerifyEnrollmentAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<TotpMfaEnrollment> EnableAsync(int userId, string code)
        {
            try
            {
                var enrollment = await RequireConfiguredEnrollmentAsync(userId);
                if (enrollment.IsTotpMfaEnabled)
                    throw new ConflictException("TOTP MFA is already enabled for this account.");

                var matchedWindow = await VerifyConfiguredCodeForManagementAsync(userId, enrollment, code, "enable");
                var replayState = await CaptureReplayGuardStateAsync(userId);
                await CheckAndUpdateReplayGuardAsync(userId, matchedWindow, true);

                TotpMfaEnrollment updated;
                try
                {
                    updated = await _repository.SetEnabledAsync(userId, true, null)
                        ?? throw new ConflictException("TOTP MFA is not configured for this account.");
                }
                catch
                {
                    await RestoreReplayGuardOnFailureAsync(userId, matchedWindow, replayState);
                    throw;
                }

                await _cacheService.DeleteKeyAsync(ActionAttemptKey(userId, "enable"));
                LogAudit(userId, "re-enable", true, "totp configuration re-enabled");
                return updated;
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                {
                    LogAudit(userId, "re-enable", false, ex.Message);
                    throw;
                }

                Logger.Error($"[TotpMfaEnrollmentService] EnableAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<TotpMfaEnrollment?> DisableAsync(int userId, string code)
        {
            try
            {
                var enrollment = await RequireConfiguredEnrollmentAsync(userId);
                if (!enrollment.IsTotpMfaEnabled)
                    throw new ConflictException("TOTP MFA is already disabled for this account.");

                var matchedWindow = await VerifyConfiguredCodeForManagementAsync(userId, enrollment, code, "disable");
                var replayState = await CaptureReplayGuardStateAsync(userId);
                await CheckAndUpdateReplayGuardAsync(userId, matchedWindow, true);

                TotpMfaEnrollment? updated;
                try
                {
                    updated = await _repository.SetEnabledAsync(userId, false, DateTime.UtcNow)
                        ?? throw new ConflictException("TOTP MFA is not configured for this account.");
                }
                catch
                {
                    await RestoreReplayGuardOnFailureAsync(userId, matchedWindow, replayState);
                    throw;
                }

                await _cacheService.DeleteKeyAsync(ActionAttemptKey(userId, "disable"));
                LogAudit(userId, "disable", true, "totp configuration disabled");
                return updated;
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                {
                    LogAudit(userId, "disable", false, ex.Message);
                    throw;
                }

                Logger.Error($"[TotpMfaEnrollmentService] DisableAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task RemoveAsync(int userId, string code)
        {
            try
            {
                var enrollment = await RequireConfiguredEnrollmentAsync(userId);
                var matchedWindow = await VerifyConfiguredCodeForManagementAsync(userId, enrollment, code, "remove");
                var replayState = await CaptureReplayGuardStateAsync(userId);
                await CheckAndUpdateReplayGuardAsync(userId, matchedWindow, true);

                try
                {
                    if (!await _repository.RemoveAsync(userId))
                        throw new ConflictException("TOTP MFA is not configured for this account.");
                }
                catch
                {
                    await RestoreReplayGuardOnFailureAsync(userId, matchedWindow, replayState);
                    throw;
                }

                await ClearManagementStateAsync(userId);
                LogAudit(userId, "remove", true, "totp configuration removed");
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                {
                    LogAudit(userId, "remove", false, ex.Message);
                    throw;
                }

                Logger.Error($"[TotpMfaEnrollmentService] RemoveAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task VerifyPersistedCodeAsync(int userId, string code)
        {
            var enrollment = await _repository.GetByUserIdAsync(userId)
                ?? throw new UnauthorizedException("TOTP MFA is not enrolled for this account.");

            if (!enrollment.IsTotpMfaEnabled)
                throw new UnauthorizedException("TOTP MFA is not enabled for this account.");

            ValidateCodeFormat(code);
            var secretBytes = Decrypt(enrollment.EncryptedSecret);
            var totp = new Totp(secretBytes);

            if (!totp.VerifyTotp(DateTime.UtcNow, code, out var matchedWindow, VerificationWindow.RfcSpecifiedNetworkDelay))
                throw new UnauthorizedException("Invalid or expired TOTP code.");

            await CheckAndUpdateReplayGuardAsync(userId, matchedWindow, false);
        }

        private async Task<TotpMfaEnrollment> RequireConfiguredEnrollmentAsync(int userId)
        {
            var enrollment = await _repository.GetByUserIdAsync(userId);
            if (enrollment == null || string.IsNullOrWhiteSpace(enrollment.EncryptedSecret))
                throw new ConflictException("TOTP MFA is not configured for this account.");

            return enrollment;
        }

        private async Task<long> VerifyConfiguredCodeForManagementAsync(int userId, TotpMfaEnrollment enrollment, string code, string action)
        {
            ValidateCodeFormat(code);
            var secretBytes = Decrypt(enrollment.EncryptedSecret);
            var totp = new Totp(secretBytes);

            if (!totp.VerifyTotp(DateTime.UtcNow, code, out var matchedWindow, VerificationWindow.RfcSpecifiedNetworkDelay))
            {
                var attempts = await RecordActionFailureAsync(userId, action);
                if (attempts >= MaxActionAttempts)
                    throw new TooManyRequestException($"Too many failed TOTP {action} attempts. Please try again later.");

                throw new BadRequestException("Invalid TOTP code.");
            }

            return matchedWindow;
        }

        private async Task<long> RecordActionFailureAsync(int userId, string action)
        {
            var key = ActionAttemptKey(userId, action);
            var attempts = await _cacheService.IncrementAsync(key);
            await _cacheService.SetExpiryAsync(key, ActionAttemptTtl);
            return attempts;
        }

        private async Task ClearManagementStateAsync(int userId)
        {
            await _cacheService.DeleteKeyAsync(ReplayGuardKey(userId));
            await _cacheService.DeleteKeyAsync(ActionAttemptKey(userId, "enable"));
            await _cacheService.DeleteKeyAsync(ActionAttemptKey(userId, "disable"));
            await _cacheService.DeleteKeyAsync(ActionAttemptKey(userId, "remove"));
        }

        private async Task<ReplayGuardState> CaptureReplayGuardStateAsync(int userId)
        {
            var key = ReplayGuardKey(userId);
            return new ReplayGuardState
            {
                Value = await _cacheService.GetValueAsync(key),
                Ttl = await _cacheService.GetTTLAsync(key),
            };
        }

        private async Task RestoreReplayGuardOnFailureAsync(int userId, long claimedWindow, ReplayGuardState previousState)
        {
            var key = ReplayGuardKey(userId);
            var currentValue = await _cacheService.GetValueAsync(key);
            if (!string.Equals(currentValue, claimedWindow.ToString(), StringComparison.Ordinal))
                return;

            if (!string.IsNullOrWhiteSpace(previousState.Value) && previousState.Ttl is TimeSpan ttl && ttl > TimeSpan.Zero)
            {
                await _cacheService.SetValueAsync(key, previousState.Value, ttl);
                return;
            }

            await _cacheService.DeleteKeyAsync(key);
        }

        private async Task CheckAndUpdateReplayGuardAsync(int userId, long matchedWindow, bool invalidAsBadRequest)
        {
            var guardKey = ReplayGuardKey(userId);
            if (!await TryClaimReplayWindowAsync(guardKey, matchedWindow))
            {
                if (invalidAsBadRequest)
                    throw new BadRequestException("TOTP code has already been used. Please wait for a new code.");

                throw new UnauthorizedException("TOTP code has already been used. Please wait for a new code.");
            }
        }

        private async Task<bool> TryClaimReplayWindowAsync(string guardKey, long matchedWindow)
        {
            var result = await _cacheService.EvalAsync(
                ReplayGuardClaimLua,
                [(RedisKey)guardKey],
                [(RedisValue)matchedWindow, (RedisValue)(long)ReplayGuardTtl.TotalMilliseconds]
            );

            return result switch
            {
                int[] values => values.Length > 0 && values[0] != 0,
                long value => value != 0,
                int value => value != 0,
                RedisResult redisResult => TryParseRedisResult(redisResult, out var parsed) && parsed != 0,
                RedisResult[] values => values.Length > 0
                    && TryParseRedisResult(values[0], out var parsed)
                    && parsed != 0,
                _ => long.TryParse(result?.ToString(), out var parsed) && parsed != 0,
            };
        }

        private static bool TryParseRedisResult(RedisResult result, out long value)
        {
            if (result.IsNull)
            {
                value = 0;
                return false;
            }

            return long.TryParse(result.ToString(), out value);
        }

        private static string Encrypt(byte[] secret)
        {
            var key = GetEncryptionKey();
            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            var ciphertext = new byte[secret.Length];
            var tag = new byte[TagSizeBytes];

            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Encrypt(nonce, secret, ciphertext, tag);

            var combined = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
            nonce.CopyTo(combined, 0);
            tag.CopyTo(combined, NonceSizeBytes);
            ciphertext.CopyTo(combined, NonceSizeBytes + TagSizeBytes);

            return "v1:" + Convert.ToBase64String(combined);
        }

        internal static byte[] Decrypt(string encrypted)
        {
            if (!encrypted.StartsWith("v1:", StringComparison.Ordinal))
                throw new InvalidOperationException("Unknown TOTP secret encryption version.");

            var combined = Convert.FromBase64String(encrypted["v1:".Length..]);
            var nonce = combined[..NonceSizeBytes];
            var tag = combined[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
            var ciphertext = combined[(NonceSizeBytes + TagSizeBytes)..];
            var plaintext = new byte[ciphertext.Length];

            var key = GetEncryptionKey();
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return plaintext;
        }

        private static byte[] GetEncryptionKey()
        {
            return Convert.FromBase64String(EnvironmentSetting.TotpEncryptionKey);
        }

        private static string BuildQrCodeUri(string secretBase32, string email)
        {
            var issuer = Uri.EscapeDataString(IssuerName);
            var account = Uri.EscapeDataString($"{IssuerName}:{email}");
            return $"otpauth://totp/{account}?secret={secretBase32}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";
        }

        private static void ValidateCodeFormat(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length != 6 || !code.All(char.IsDigit))
                throw new BadRequestException("TOTP code must be exactly 6 digits.");
        }

        private static void EnsureEnrollmentAvailable()
        {
            if (!EnvironmentSetting.AuthTotpMfaEnrollmentEnabled)
                throw new NotAvailableException("TOTP MFA enrollment is currently unavailable.");
        }

        private static void LogAudit(int userId, string action, bool success, string reason)
        {
            Logger.Info($"[AuthMfaAudit] userId={userId} method=totp action={action} success={success} reason={reason}");
        }

        private static string PendingKey(int userId) => $"totp:enrollment:pending:user:{userId}";
        private static string VerifyLockKey(int userId) => $"totp:enrollment:verify-lock:user:{userId}";
        private static string ReplayGuardKey(int userId) => $"totp:lastused:{userId}";
        private static string ActionAttemptKey(int userId, string action) => $"totp:action:attempt:{action}:{userId}";

        private sealed class PendingEnrollmentState
        {
            public required string SecretBase32
            {
                get; set;
            }

            public int FailedAttempts
            {
                get; set;
            }
        }

        private sealed class ReplayGuardState
        {
            public string? Value
            {
                get; set;
            }

            public TimeSpan? Ttl
            {
                get; set;
            }
        }
    }
}
