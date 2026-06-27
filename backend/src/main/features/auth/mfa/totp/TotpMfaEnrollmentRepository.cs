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
    }
}
