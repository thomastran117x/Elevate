using backend.main.infrastructure.database.core;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace backend.main.features.auth.oauth.links
{
    public sealed class UserIdentityLinkRepository : IUserIdentityLinkRepository
    {
        private const string SubjectUniqueIndexName = "IX_UserIdentityLinks_Provider_Subject";
        private const string ProviderUniqueIndexName = "IX_UserIdentityLinks_UserId_Provider";

        private readonly AppDatabaseContext _context;

        public UserIdentityLinkRepository(AppDatabaseContext context)
        {
            _context = context;
        }

        public async Task<UserIdentityLink?> FindBySubjectAsync(
            string provider,
            string subject,
            CancellationToken cancellationToken = default)
        {
            return await _context.UserIdentityLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Provider == provider && l.Subject == subject, cancellationToken);
        }

        public async Task<IReadOnlyList<UserIdentityLink>> ListForUserAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            return await _context.UserIdentityLinks
                .AsNoTracking()
                .Where(l => l.UserId == userId)
                .OrderBy(l => l.Provider)
                .ToListAsync(cancellationToken);
        }

        public async Task<IdentityLinkAddResult> AddAsync(
            UserIdentityLink link,
            CancellationToken cancellationToken = default)
        {
            link.Email = UserIdentityLink.ClampMetadata(link.Email, UserIdentityLink.EmailMaxLength);
            link.DisplayName = UserIdentityLink.ClampMetadata(link.DisplayName, UserIdentityLink.DisplayNameMaxLength);

            try
            {
                await _context.UserIdentityLinks.AddAsync(link, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                return IdentityLinkAddResult.Added;
            }
            catch (DbUpdateException exception) when (ClassifyUniqueViolation(exception) is { } result)
            {
                _context.ChangeTracker.Clear();
                return result;
            }
        }

        public async Task TouchAsync(
            int linkId,
            string? email,
            string? displayName,
            DateTime usedAtUtc,
            CancellationToken cancellationToken = default)
        {
            // Blank metadata keeps what was stored: a provider that omits a claim on one sign-in
            // has not reported that the value changed.
            email = UserIdentityLink.ClampMetadata(email, UserIdentityLink.EmailMaxLength);
            displayName = UserIdentityLink.ClampMetadata(displayName, UserIdentityLink.DisplayNameMaxLength);

            await _context.UserIdentityLinks
                .Where(l => l.Id == linkId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(l => l.LastUsedAtUtc, usedAtUtc)
                    .SetProperty(l => l.Email, l => email ?? l.Email)
                    .SetProperty(l => l.DisplayName, l => displayName ?? l.DisplayName),
                    cancellationToken);
        }

        public async Task<IdentityLinkRemoveResult> RemoveIfAnotherMethodRemainsAsync(
            int userId,
            string provider,
            CancellationToken cancellationToken = default)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    // Two removals each counting the other's link as remaining would otherwise
                    // leave an account with neither. The account row serializes them.
                    if (_context.Database.IsNpgsql())
                    {
                        await _context.Users
                            .FromSqlInterpolated(
                                $"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE")
                            .AsNoTracking()
                            .SingleOrDefaultAsync(cancellationToken);
                    }

                    var links = await _context.UserIdentityLinks
                        .Where(l => l.UserId == userId)
                        .Select(l => new { l.Id, l.Provider })
                        .ToListAsync(cancellationToken);
                    var target = links.FirstOrDefault(l => l.Provider == provider);
                    if (target == null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return IdentityLinkRemoveResult.NotLinked;
                    }

                    var hasPassword = await _context.Users
                        .AnyAsync(u => u.Id == userId && u.Password != null, cancellationToken);
                    if (!hasPassword && links.Count == 1)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return IdentityLinkRemoveResult.LastSignInMethod;
                    }

                    await _context.UserIdentityLinks
                        .Where(l => l.Id == target.Id)
                        .ExecuteDeleteAsync(cancellationToken);

                    // In the same commit as the removal, so nothing the removed provider signed in
                    // outlives it, not even for the moment before the sessions are swept.
                    await _context.Users
                        .Where(u => u.Id == userId)
                        .ExecuteUpdateAsync(
                            set => set.SetProperty(u => u.AuthVersion, u => u.AuthVersion + 1),
                            cancellationToken);

                    await transaction.CommitAsync(cancellationToken);
                    return IdentityLinkRemoveResult.Removed;
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            });
        }

        /// <summary>
        /// Which unique index an insert violated, or null for any other failure. Postgres names
        /// the index; SQLite, used by the repository tests, only names the columns.
        /// </summary>
        private static IdentityLinkAddResult? ClassifyUniqueViolation(DbUpdateException exception)
        {
            switch (exception.InnerException)
            {
                case PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres:
                    if (string.Equals(postgres.ConstraintName, SubjectUniqueIndexName, StringComparison.Ordinal))
                        return IdentityLinkAddResult.SubjectTaken;
                    if (string.Equals(postgres.ConstraintName, ProviderUniqueIndexName, StringComparison.Ordinal))
                        return IdentityLinkAddResult.ProviderTaken;
                    return null;

                case SqliteException { SqliteErrorCode: 19 } sqlite:
                    if (sqlite.Message.Contains("UserIdentityLinks.Subject", StringComparison.OrdinalIgnoreCase))
                        return IdentityLinkAddResult.SubjectTaken;
                    if (sqlite.Message.Contains("UserIdentityLinks.UserId", StringComparison.OrdinalIgnoreCase))
                        return IdentityLinkAddResult.ProviderTaken;
                    return null;

                default:
                    return null;
            }
        }
    }
}
