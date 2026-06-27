using System.Security.Cryptography;
using System.Text;

using backend.main.application.environment;
using backend.main.features.auth.contracts.responses;
using backend.main.features.cache;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

using Newtonsoft.Json;

using OtpNet;

namespace backend.main.features.auth.mfa.totp
{
    public sealed class TotpMfaEnrollmentService : ITotpMfaEnrollmentService
    {
        private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan VerifyLockTtl = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ReplayGuardTtl = TimeSpan.FromSeconds(90);
        private const int MaxFailedAttempts = 5;
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

                var secretBytes = KeyGeneration.GenerateRandomKey(SecretSizeBytes);
                var secretBase32 = Base32Encoding.ToString(secretBytes);
                var expiresAtUtc = DateTime.UtcNow.Add(PendingTtl);

                var state = new PendingEnrollmentState
                {
                    SecretBase32 = secretBase32,
                    FailedAttempts = 0,
                };

                await _cacheService.SetValueAsync(
                    PendingKey(userId),
                    JsonConvert.SerializeObject(state),
                    PendingTtl
                );

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

                    await CheckAndUpdateReplayGuardAsync(userId, matchedWindow);

                    var encryptedSecret = Encrypt(secretBytes);
                    var enrolledAtUtc = DateTime.UtcNow;
                    var enrollment = await _repository.UpsertAsync(userId, encryptedSecret, EncryptionKeyVersion, enrolledAtUtc);

                    await _cacheService.DeleteKeyAsync(PendingKey(userId));

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
                    throw;

                Logger.Error($"[TotpMfaEnrollmentService] VerifyEnrollmentAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<TotpMfaEnrollment?> DisableAsync(int userId, string code)
        {
            try
            {
                EnsureEnrollmentAvailable();

                var enrollment = await _repository.GetByUserIdAsync(userId);
                if (enrollment == null || !enrollment.IsTotpMfaEnabled)
                    return enrollment;

                ValidateCodeFormat(code);
                var secretBytes = Decrypt(enrollment.EncryptedSecret);
                var totp = new Totp(secretBytes);

                if (!totp.VerifyTotp(DateTime.UtcNow, code, out var matchedWindow, VerificationWindow.RfcSpecifiedNetworkDelay))
                    throw new UnauthorizedException("Invalid TOTP code. Cannot disable TOTP MFA.");

                await CheckAndUpdateReplayGuardAsync(userId, matchedWindow);

                return await _repository.SetEnabledAsync(userId, false, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[TotpMfaEnrollmentService] DisableAsync failed: {ex}");
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

            await CheckAndUpdateReplayGuardAsync(userId, matchedWindow);
        }

        private async Task CheckAndUpdateReplayGuardAsync(int userId, long matchedWindow)
        {
            var guardKey = ReplayGuardKey(userId);
            var stored = await _cacheService.GetValueAsync(guardKey);

            if (stored != null && long.TryParse(stored, out var lastWindow) && matchedWindow <= lastWindow)
                throw new UnauthorizedException("TOTP code has already been used. Please wait for a new code.");

            await _cacheService.SetValueAsync(guardKey, matchedWindow.ToString(), ReplayGuardTtl);
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

        private static string PendingKey(int userId) => $"totp:enrollment:pending:user:{userId}";
        private static string VerifyLockKey(int userId) => $"totp:enrollment:verify-lock:user:{userId}";
        private static string ReplayGuardKey(int userId) => $"totp:lastused:{userId}";

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
    }
}
