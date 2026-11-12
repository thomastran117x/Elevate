using System.Security.Cryptography;

using backend.main.features.auth.oauth.links;
using backend.main.infrastructure.database.core;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace backend.main.features.auth.passkeys
{
    public sealed class PasskeyCredentialRepository : IPasskeyCredentialRepository
    {
        public const int UserHandleBytes = 32;

        private const string CredentialIdUniqueIndexName = "IX_PasskeyCredentials_CredentialId";

        private readonly AppDatabaseContext _context;

        public PasskeyCredentialRepository(AppDatabaseContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<PasskeyCredential>> ListForUserAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            return await _context.PasskeyCredentials
                .AsNoTracking()
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.CreatedAtUtc)
                .ThenBy(c => c.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<PasskeyCredential?> FindByCredentialIdAsync(
            byte[] credentialId,
            CancellationToken cancellationToken = default)
        {
            return await _context.PasskeyCredentials
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CredentialId == credentialId, cancellationToken);
        }

        public async Task<PasskeyFactorSummary> GetFactorSummaryAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            var summary = await _context.PasskeyCredentials
                .AsNoTracking()
                .Where(c => c.UserId == userId)
                .GroupBy(c => c.UserId)
                .Select(g => new { Count = g.Count(), Newest = g.Max(c => c.CreatedAtUtc) })
                .FirstOrDefaultAsync(cancellationToken);

            return summary == null
                ? new PasskeyFactorSummary(0, null)
                : new PasskeyFactorSummary(summary.Count, summary.Newest);
        }

        public async Task<byte[]?> GetOrCreateUserHandleAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            var handle = RandomNumberGenerator.GetBytes(UserHandleBytes);

            // Conditional, so a handle is never replaced once passkeys carry it; the read below
            // returns whichever concurrent first call won.
            await _context.Users
                .Where(u => u.Id == userId && u.PasskeyUserHandle == null)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.PasskeyUserHandle, handle), cancellationToken);

            return await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.PasskeyUserHandle)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<PasskeyAddResult> AddAsync(
            PasskeyCredential credential,
            int maxPerUser,
            CancellationToken cancellationToken = default)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    if (!await LockUserAsync(credential.UserId, cancellationToken))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return PasskeyAddResult.UserNotFound;
                    }

                    var count = await _context.PasskeyCredentials
                        .CountAsync(c => c.UserId == credential.UserId, cancellationToken);
                    if (count >= maxPerUser)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return PasskeyAddResult.LimitReached;
                    }

                    await _context.PasskeyCredentials.AddAsync(credential, cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return PasskeyAddResult.Added;
                }
                catch (DbUpdateException exception) when (IsCredentialIdViolation(exception))
                {
                    _context.ChangeTracker.Clear();
                    await transaction.RollbackAsync(CancellationToken.None);
                    return PasskeyAddResult.CredentialTaken;
                }
                catch
                {
                    _context.ChangeTracker.Clear();
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            });
        }

        public async Task<bool> RenameAsync(
            int userId,
            int passkeyId,
            string friendlyName,
            CancellationToken cancellationToken = default)
        {
            var updated = await _context.PasskeyCredentials
                .Where(c => c.Id == passkeyId && c.UserId == userId)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.FriendlyName, friendlyName), cancellationToken);
            return updated > 0;
        }

        public async Task RecordUseAsync(
            int passkeyId,
            long signatureCounter,
            bool isBackedUp,
            DateTime usedAtUtc,
            bool counterAnomaly,
            CancellationToken cancellationToken = default)
        {
            await _context.PasskeyCredentials
                .Where(c => c.Id == passkeyId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(
                        c => c.SignatureCounter,
                        c => c.SignatureCounter > signatureCounter ? c.SignatureCounter : signatureCounter)
                    .SetProperty(c => c.IsBackedUp, isBackedUp)
                    .SetProperty(c => c.LastUsedAtUtc, usedAtUtc)
                    .SetProperty(
                        c => c.CounterAnomalyAtUtc,
                        c => counterAnomaly ? usedAtUtc : c.CounterAnomalyAtUtc),
                    cancellationToken);
        }

        public async Task FlagCounterAnomalyAsync(
            int passkeyId,
            DateTime detectedAtUtc,
            CancellationToken cancellationToken = default)
        {
            await _context.PasskeyCredentials
                .Where(c => c.Id == passkeyId)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.CounterAnomalyAtUtc, detectedAtUtc), cancellationToken);
        }

        public async Task<PasskeyRemoveResult> RemoveIfAnotherMethodRemainsAsync(
            int userId,
            int passkeyId,
            CancellationToken cancellationToken = default)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    // Two removals each counting the other's passkey as remaining would otherwise
                    // leave an account with neither. Provider unlinks take the same lock.
                    await LockUserAsync(userId, cancellationToken);

                    var passkeys = await _context.PasskeyCredentials
                        .Where(c => c.UserId == userId)
                        .Select(c => c.Id)
                        .ToListAsync(cancellationToken);
                    if (!passkeys.Contains(passkeyId))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return PasskeyRemoveResult.NotFound;
                    }

                    var hasPassword = await _context.Users
                        .AnyAsync(u => u.Id == userId && u.Password != null, cancellationToken);
                    var linkCount = await _context.UserIdentityLinks
                        .CountAsync(l => l.UserId == userId, cancellationToken);
                    if (!SignInMethods.RemainAfterRemoving(hasPassword, linkCount, passkeys.Count))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return PasskeyRemoveResult.LastSignInMethod;
                    }

                    await _context.PasskeyCredentials
                        .Where(c => c.Id == passkeyId && c.UserId == userId)
                        .ExecuteDeleteAsync(cancellationToken);

                    await transaction.CommitAsync(cancellationToken);
                    return PasskeyRemoveResult.Removed;
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            });
        }

        /// <summary>
        /// Takes the account row lock that serializes changes to its sign-in methods. SQLite,
        /// used by the repository tests, has no row locks and serializes writers itself.
        /// </summary>
        private async Task<bool> LockUserAsync(int userId, CancellationToken cancellationToken)
        {
            if (_context.Database.IsNpgsql())
            {
                var locked = await _context.Users
                    .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(cancellationToken);
                return locked != null;
            }

            return await _context.Users.AnyAsync(u => u.Id == userId, cancellationToken);
        }

        /// <summary>
        /// Whether an insert hit the credential id index. Postgres names the index; SQLite only
        /// names the column.
        /// </summary>
        private static bool IsCredentialIdViolation(DbUpdateException exception) =>
            exception.InnerException switch
            {
                PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres =>
                    string.Equals(postgres.ConstraintName, CredentialIdUniqueIndexName, StringComparison.Ordinal),
                SqliteException { SqliteErrorCode: 19 } sqlite =>
                    sqlite.Message.Contains("PasskeyCredentials.CredentialId", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
    }
}
