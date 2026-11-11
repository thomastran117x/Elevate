using backend.main.features.auth.events;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.auth.sessions;

public sealed class AuthSessionRepository : IAuthSessionRepository
{
    private readonly AppDatabaseContext _context;

    public AuthSessionRepository(AppDatabaseContext context) => _context = context;

    public async Task<bool> RecordUseAsync(AuthSession session)
    {
        // Conditional, so a revocation that landed after the refresh was validated is never
        // overwritten or undone: the update matches only a row that is still alive.
        var updated = await _context.AuthSessions
            .Where(s => s.Id == session.Id
                && s.UserId == session.UserId
                && s.RevokedAt == null
                && s.ExpiresAt > session.LastSeenAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.LastSeenAt, session.LastSeenAt)
                .SetProperty(s => s.ExpiresAt, session.ExpiresAt)
                .SetProperty(s => s.RememberMe, session.RememberMe)
                .SetProperty(s => s.Client, session.Client)
                .SetProperty(s => s.DeviceClass, session.DeviceClass)
                .SetProperty(s => s.Network, session.Network));
        if (updated > 0)
            return true;

        // A row that exists but did not match is revoked, expired, or another account's. It is
        // never replaced, or a revoked session could be brought back by its own refresh.
        if (await _context.AuthSessions.AsNoTracking().AnyAsync(s => s.Id == session.Id))
            return false;

        _context.AuthSessions.Add(session);
        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        finally
        {
            _context.Entry(session).State = EntityState.Detached;
        }
    }

    public async Task<AccessTokenSessionState?> GetAccessTokenStateAsync(
        int userId,
        string sessionId,
        DateTime now)
    {
        return await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new AccessTokenSessionState(
                u.IsDisabled,
                u.AuthVersion,
                _context.AuthSessions.Any(s =>
                    s.Id == sessionId
                    && s.UserId == userId
                    && s.RevokedAt == null
                    && s.ExpiresAt > now)))
            .FirstOrDefaultAsync();
    }

    public async Task<List<AuthSession>> ListActiveAsync(int userId, DateTime now)
    {
        return await _context.AuthSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt)
            .ThenBy(s => s.Id)
            .ToListAsync();
    }

    public async Task<int?> RevokeAsync(string sessionId, AuthSecurityReason reason, DateTime now)
    {
        var userId = await _context.AuthSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .Select(s => (int?)s.UserId)
            .FirstOrDefaultAsync();
        if (userId is null)
            return null;

        var revoked = await RevokeWhereAsync(
            _context.AuthSessions.Where(s => s.Id == sessionId && s.RevokedAt == null),
            reason,
            now);
        return revoked > 0 ? userId : null;
    }

    public async Task<bool> RevokeForUserAsync(
        int userId,
        string sessionId,
        AuthSecurityReason reason,
        DateTime now)
    {
        var revoked = await RevokeWhereAsync(
            _context.AuthSessions.Where(s =>
                s.Id == sessionId
                && s.UserId == userId
                && s.RevokedAt == null
                && s.ExpiresAt > now),
            reason,
            now);
        return revoked > 0;
    }

    public async Task<IReadOnlyList<string>> RevokeAllAsync(
        int userId,
        AuthSecurityReason reason,
        DateTime now,
        string? exceptSessionId = null)
    {
        var live = _context.AuthSessions.Where(s => s.UserId == userId && s.RevokedAt == null);
        if (exceptSessionId is not null)
            live = live.Where(s => s.Id != exceptSessionId);

        var ids = await live.AsNoTracking().Select(s => s.Id).ToListAsync();
        if (ids.Count == 0)
            return ids;

        await RevokeWhereAsync(
            _context.AuthSessions.Where(s => ids.Contains(s.Id) && s.RevokedAt == null),
            reason,
            now);
        return ids;
    }

    public async Task<int> DeleteExpiredAsync(DateTime cutoff, int batchSize)
    {
        var ids = await _context.AuthSessions
            .AsNoTracking()
            .Where(s => s.ExpiresAt < cutoff)
            .OrderBy(s => s.ExpiresAt)
            .Take(batchSize)
            .Select(s => s.Id)
            .ToListAsync();
        if (ids.Count == 0)
            return 0;

        return await _context.AuthSessions
            .Where(s => ids.Contains(s.Id))
            .ExecuteDeleteAsync();
    }

    private static Task<int> RevokeWhereAsync(
        IQueryable<AuthSession> sessions,
        AuthSecurityReason reason,
        DateTime now) =>
        sessions.ExecuteUpdateAsync(setters => setters
            .SetProperty(s => s.RevokedAt, now)
            .SetProperty(s => s.RevocationReason, reason));
}
