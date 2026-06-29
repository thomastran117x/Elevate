using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.auth.mfa
{
    public sealed class MfaEnrollmentRepository : IMfaEnrollmentRepository
    {
        private readonly AppDatabaseContext _context;

        public MfaEnrollmentRepository(AppDatabaseContext context)
        {
            _context = context;
        }

        public async Task<SmsMfaEnrollment?> GetByUserIdAsync(int userId)
        {
            return await _context.SmsMfaEnrollments
                .AsNoTracking()
                .FirstOrDefaultAsync(enrollment => enrollment.UserId == userId);
        }

        public async Task<SmsMfaEnrollment> UpsertVerifiedPhoneAsync(
            int userId,
            string phoneNumber,
            DateTime verifiedAtUtc
        )
        {
            var existing = await _context.SmsMfaEnrollments.FindAsync(userId);
            if (existing == null)
            {
                existing = new SmsMfaEnrollment
                {
                    UserId = userId,
                    PhoneNumber = phoneNumber,
                    IsSmsMfaEnabled = true,
                    PhoneVerifiedAtUtc = verifiedAtUtc,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };

                await _context.SmsMfaEnrollments.AddAsync(existing);
            }
            else
            {
                existing.PhoneNumber = phoneNumber;
                existing.IsSmsMfaEnabled = true;
                existing.PhoneVerifiedAtUtc = verifiedAtUtc;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<SmsMfaEnrollment?> SetEnabledAsync(int userId, bool isEnabled)
        {
            var existing = await _context.SmsMfaEnrollments.FindAsync(userId);
            if (existing == null)
                return null;

            existing.IsSmsMfaEnabled = isEnabled;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> RemoveAsync(int userId)
        {
            var existing = await _context.SmsMfaEnrollments.FindAsync(userId);
            if (existing == null)
                return false;

            _context.SmsMfaEnrollments.Remove(existing);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
