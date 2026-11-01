using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.auth.mfa.recovery
{
    public sealed class RecoveryCodeRepository : IRecoveryCodeRepository
    {
        private readonly AppDatabaseContext _context;

        public RecoveryCodeRepository(AppDatabaseContext context)
        {
            _context = context;
        }

        public async Task<RecoveryCodeStatus> GetStatusAsync(int userId)
        {
            var summary = await _context.RecoveryCodes
                .AsNoTracking()
                .Where(c => c.UserId == userId)
                .GroupBy(c => c.UserId)
                .Select(g => new
                {
                    Remaining = g.Count(c => c.UsedAtUtc == null),
                    GeneratedAtUtc = g.Min(c => c.CreatedAtUtc),
                })
                .FirstOrDefaultAsync();

            return summary == null
                ? new RecoveryCodeStatus(0, null)
                : new RecoveryCodeStatus(summary.Remaining, summary.GeneratedAtUtc);
        }

        public async Task<bool> HasUnusedAsync(int userId)
        {
            return await _context.RecoveryCodes
                .AsNoTracking()
                .AnyAsync(c => c.UserId == userId && c.UsedAtUtc == null);
        }

        public async Task<DateTime?> GetSetGeneratedAtAsync(int userId)
        {
            return await _context.RecoveryCodes
                .AsNoTracking()
                .Where(c => c.UserId == userId)
                .MaxAsync(c => (DateTime?)c.CreatedAtUtc);
        }

        public async Task<RecoveryCode?> FindUnusedAsync(int userId, string selector)
        {
            return await _context.RecoveryCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.UserId == userId &&
                    c.Selector == selector &&
                    c.UsedAtUtc == null);
        }

        public async Task<bool> TryConsumeAsync(int codeId, int userId, DateTime usedAtUtc)
        {
            var affected = await _context.RecoveryCodes
                .Where(c => c.Id == codeId && c.UserId == userId && c.UsedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.UsedAtUtc, usedAtUtc));
            return affected == 1;
        }

        public async Task<bool> TryReleaseAsync(int codeId, int userId, DateTime usedAtUtc)
        {
            var affected = await _context.RecoveryCodes
                .Where(c => c.Id == codeId && c.UserId == userId && c.UsedAtUtc == usedAtUtc)
                .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.UsedAtUtc, (DateTime?)null));
            return affected == 1;
        }

        public async Task<bool> ReplaceSetAsync(int userId, IReadOnlyList<RecoveryCode> codes)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Two regenerations each deleting before either inserts would otherwise leave
                    // both new sets behind. The account row serializes them.
                    if (_context.Database.IsNpgsql())
                    {
                        await _context.Users
                            .FromSqlInterpolated(
                                $"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE")
                            .AsNoTracking()
                            .SingleOrDefaultAsync();
                    }

                    var removed = await _context.RecoveryCodes
                        .Where(c => c.UserId == userId)
                        .ExecuteDeleteAsync();

                    foreach (var code in codes)
                        code.UserId = userId;

                    await _context.RecoveryCodes.AddRangeAsync(codes);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return removed > 0;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    _context.ChangeTracker.Clear();
                    throw;
                }
            });
        }
    }
}
