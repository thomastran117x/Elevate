using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.auth.history;

public sealed class SecurityHistoryRepository : ISecurityHistoryRepository
{
    private readonly AppDatabaseContext _context;

    public SecurityHistoryRepository(AppDatabaseContext context) => _context = context;

    public async Task<(IReadOnlyList<AccountSecurityEvent> Items, int TotalCount)> GetPageAsync(
        int userId,
        DateTime now,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.AccountSecurityEvents
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.ExpiresAtUtc > now);

        var totalCount = await query.CountAsync(cancellationToken);
        if (totalCount == 0)
            return ([], 0);

        var items = await query
            // Id breaks ties between events recorded in the same instant, so paging is stable.
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
