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

        public async Task<UserIdentityLink?> FindBySubjectAsync(string provider, string subject)
        {
            return await _context.UserIdentityLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Provider == provider && l.Subject == subject);
        }

        public async Task<IReadOnlyList<UserIdentityLink>> ListForUserAsync(int userId)
        {
            return await _context.UserIdentityLinks
                .AsNoTracking()
                .Where(l => l.UserId == userId)
                .OrderBy(l => l.Provider)
                .ToListAsync();
        }

        public async Task<IdentityLinkAddResult> AddAsync(UserIdentityLink link)
        {
            try
            {
                await _context.UserIdentityLinks.AddAsync(link);
                await _context.SaveChangesAsync();
                return IdentityLinkAddResult.Added;
            }
            catch (DbUpdateException exception) when (ClassifyUniqueViolation(exception) is { } result)
            {
                _context.ChangeTracker.Clear();
                return result;
            }
        }

        public async Task TouchAsync(int linkId, string? email, string? displayName, DateTime usedAtUtc)
        {
            // Blank metadata keeps what was stored: a provider that omits a claim on one sign-in
            // has not reported that the value changed.
            email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
            displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

            await _context.UserIdentityLinks
                .Where(l => l.Id == linkId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(l => l.LastUsedAtUtc, usedAtUtc)
                    .SetProperty(l => l.Email, l => email ?? l.Email)
                    .SetProperty(l => l.DisplayName, l => displayName ?? l.DisplayName));
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
