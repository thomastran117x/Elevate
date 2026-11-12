using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.auth.mfa.totp
{
    public sealed class TotpMfaEnrollmentRepository : ITotpMfaEnrollmentRepository
    {
        private readonly AppDatabaseContext _context;

        public TotpMfaEnrollmentRepository(AppDatabaseContext context)
        {
            _context = context;
        }

        public async Task<TotpMfaEnrollment?> GetByUserIdAsync(int userId)
        {
            return await _context.TotpMfaEnrollments
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == userId);
        }

        public async Task<TotpMfaEnrollment> UpsertAsync(
            int userId,
            string encryptedSecret,
            int keyVersion,
            DateTime enrolledAtUtc
        )
        {
            var existing = await _context.TotpMfaEnrollments.FindAsync(userId);
            if (existing == null)
            {
                existing = new TotpMfaEnrollment
                {
                    UserId = userId,
                    EncryptedSecret = encryptedSecret,
                    EncryptionKeyVersion = keyVersion,
                    IsTotpMfaEnabled = true,
                    EnrolledAtUtc = enrolledAtUtc,
                    DisabledAtUtc = null,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
                await _context.TotpMfaEnrollments.AddAsync(existing);
            }
            else
            {
                existing.EncryptedSecret = encryptedSecret;
                existing.EncryptionKeyVersion = keyVersion;
                existing.IsTotpMfaEnabled = true;
                existing.EnrolledAtUtc = enrolledAtUtc;
                existing.DisabledAtUtc = null;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<TotpMfaEnrollment?> SetEnabledAsync(int userId, bool isEnabled, DateTime? disabledAtUtc)
        {
            var existing = await _context.TotpMfaEnrollments.FindAsync(userId);
            if (existing == null)
                return null;

            existing.IsTotpMfaEnabled = isEnabled;
            existing.DisabledAtUtc = disabledAtUtc;
            existing.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> RemoveAsync(int userId)
        {
            var existing = await _context.TotpMfaEnrollments.FindAsync(userId);
            if (existing == null)
                return false;

            _context.TotpMfaEnrollments.Remove(existing);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IReadOnlyList<TotpStoredSecret>> ListSecretsNotUnderKeyAsync(
            string activePrefix,
            int afterUserId,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            return await _context.TotpMfaEnrollments
                .AsNoTracking()
                .Where(e => e.UserId > afterUserId && !e.EncryptedSecret.StartsWith(activePrefix))
                .OrderBy(e => e.UserId)
                .Take(batchSize)
                .Select(e => new TotpStoredSecret(e.UserId, e.EncryptedSecret))
                .ToListAsync(cancellationToken);
        }

        public Task<int> CountSecretsNotUnderKeyAsync(string activePrefix, CancellationToken cancellationToken = default)
        {
            return _context.TotpMfaEnrollments
                .CountAsync(e => !e.EncryptedSecret.StartsWith(activePrefix), cancellationToken);
        }

        public async Task<bool> ReplaceSecretAsync(
            int userId,
            string expectedEncryptedSecret,
            string newEncryptedSecret,
            int keyVersion,
            CancellationToken cancellationToken = default)
        {
            // A single conditional UPDATE: replicas sweeping at once, or a user re-enrolling or
            // removing TOTP meanwhile, make this match nothing rather than overwrite their change.
            var updated = await _context.TotpMfaEnrollments
                .Where(e => e.UserId == userId && e.EncryptedSecret == expectedEncryptedSecret)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(e => e.EncryptedSecret, newEncryptedSecret)
                        .SetProperty(e => e.EncryptionKeyVersion, keyVersion),
                    cancellationToken);

            return updated == 1;
        }
    }
}
