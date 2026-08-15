using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.clubs.discussions.replies;

public sealed class ClubDiscussionReplyRepository : IClubDiscussionReplyRepository
{
    private readonly AppDatabaseContext _context;

    public ClubDiscussionReplyRepository(AppDatabaseContext context) => _context = context;

    public async Task<ClubDiscussionReply> CreateAsync(ClubDiscussionReply reply)
    {
        _context.ClubDiscussionReplies.Add(reply);
        await _context.SaveChangesAsync();
        return reply;
    }

    public Task<ClubDiscussionReply?> GetByIdAsync(int id) =>
        _context.ClubDiscussionReplies.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);

    public async Task<(List<ClubDiscussionReply> Items, bool HasMore)> GetPageAsync(
        int discussionId, int? parentReplyId, DiscussionReplySort sort,
        DiscussionReplyCursor? cursor, int pageSize)
    {
        var query = _context.ClubDiscussionReplies.AsNoTracking()
            .Where(r => r.DiscussionId == discussionId && r.ParentReplyId == parentReplyId);

        if (cursor is not null)
        {
            query = sort == DiscussionReplySort.Newest
                ? query.Where(r => r.CreatedAt < cursor.CreatedAt ||
                    (r.CreatedAt == cursor.CreatedAt && r.Id < cursor.Id))
                : query.Where(r => r.CreatedAt > cursor.CreatedAt ||
                    (r.CreatedAt == cursor.CreatedAt && r.Id > cursor.Id));
        }

        query = sort == DiscussionReplySort.Newest
            ? query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            : query.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id);

        var items = await query.Take(pageSize + 1).ToListAsync();
        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);
        return (items, hasMore);
    }

    public Task<int> CountByParentAsync(int discussionId, int? parentReplyId) =>
        _context.ClubDiscussionReplies.AsNoTracking()
            .CountAsync(r => r.DiscussionId == discussionId && r.ParentReplyId == parentReplyId);

    public async Task<Dictionary<int, int>> CountByDiscussionIdsAsync(IEnumerable<int> discussionIds)
    {
        var ids = discussionIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await _context.ClubDiscussionReplies.AsNoTracking()
            .Where(r => ids.Contains(r.DiscussionId))
            .GroupBy(r => r.DiscussionId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);
    }

    public async Task<Dictionary<int, int>> GetDirectReplyCountsAsync(IEnumerable<int> replyIds)
    {
        var ids = replyIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await _context.ClubDiscussionReplies.AsNoTracking()
            .Where(r => r.ParentReplyId.HasValue && ids.Contains(r.ParentReplyId.Value))
            .GroupBy(r => r.ParentReplyId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);
    }

    public async Task<Dictionary<int, DiscussionReplyReactionSummary>> GetReactionSummariesAsync(
        IEnumerable<int> replyIds, int? currentUserId)
    {
        var ids = replyIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        var counts = await _context.ClubDiscussionReplyReactions.AsNoTracking()
            .Where(r => ids.Contains(r.ReplyId))
            .GroupBy(r => r.ReplyId)
            .Select(g => new
            {
                ReplyId = g.Key,
                Likes = g.Count(r => r.Reaction == DiscussionReplyReactionType.Like),
                Dislikes = g.Count(r => r.Reaction == DiscussionReplyReactionType.Dislike)
            })
            .ToDictionaryAsync(x => x.ReplyId);

        Dictionary<int, DiscussionReplyReactionType> mine = [];
        if (currentUserId.HasValue)
        {
            mine = await _context.ClubDiscussionReplyReactions.AsNoTracking()
                .Where(r => r.UserId == currentUserId.Value && ids.Contains(r.ReplyId))
                .ToDictionaryAsync(r => r.ReplyId, r => r.Reaction);
        }

        return ids.ToDictionary(
            id => id,
            id => new DiscussionReplyReactionSummary(
                counts.GetValueOrDefault(id)?.Likes ?? 0,
                counts.GetValueOrDefault(id)?.Dislikes ?? 0,
                mine.TryGetValue(id, out var reaction) ? reaction : null));
    }

    public async Task<ClubDiscussionReply?> UpdateAsync(int id, string content)
    {
        var reply = await _context.ClubDiscussionReplies.FindAsync(id);
        if (reply is null) return null;
        reply.Content = content;
        reply.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return reply;
    }

    public async Task<ClubDiscussionReply?> SoftDeleteAsync(int id)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var reply = await _context.ClubDiscussionReplies.FindAsync(id);
        if (reply is null) return null;
        if (!reply.IsDeleted)
        {
            reply.IsDeleted = true;
            reply.DeletedAt = DateTime.UtcNow;
            reply.UpdatedAt = reply.DeletedAt.Value;
            reply.Content = string.Empty;
            await _context.ClubDiscussionReplyReactions
                .Where(r => r.ReplyId == id)
                .ExecuteDeleteAsync();
            await _context.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        return reply;
    }

    public async Task<DiscussionReplyReactionSummary> SetReactionAsync(
        int replyId, int userId, DiscussionReplyReactionType reaction)
    {
        var existing = await _context.ClubDiscussionReplyReactions
            .SingleOrDefaultAsync(r => r.ReplyId == replyId && r.UserId == userId);
        ClubDiscussionReplyReaction? created = null;
        if (existing is null)
        {
            created = new ClubDiscussionReplyReaction
            {
                ReplyId = replyId,
                UserId = userId,
                Reaction = reaction
            };
            _context.ClubDiscussionReplyReactions.Add(created);
        }
        else if (existing.Reaction != reaction)
        {
            existing.Reaction = reaction;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException) when (created is not null)
        {
            // The composite key makes concurrent double-taps safe. If another request inserted
            // first, detach our losing insert and converge the persisted row on the requested value.
            _context.Entry(created).State = EntityState.Detached;
            existing = await _context.ClubDiscussionReplyReactions
                .SingleAsync(r => r.ReplyId == replyId && r.UserId == userId);
            if (existing.Reaction != reaction)
            {
                existing.Reaction = reaction;
                existing.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
        return await GetReactionSummaryAsync(replyId, userId);
    }

    public async Task<DiscussionReplyReactionSummary> ClearReactionAsync(int replyId, int userId)
    {
        var existing = await _context.ClubDiscussionReplyReactions
            .SingleOrDefaultAsync(r => r.ReplyId == replyId && r.UserId == userId);
        if (existing is not null)
        {
            _context.ClubDiscussionReplyReactions.Remove(existing);
            await _context.SaveChangesAsync();
        }
        return await GetReactionSummaryAsync(replyId, userId);
    }

    private async Task<DiscussionReplyReactionSummary> GetReactionSummaryAsync(int replyId, int userId)
    {
        var reactions = await _context.ClubDiscussionReplyReactions.AsNoTracking()
            .Where(r => r.ReplyId == replyId)
            .ToListAsync();
        return new DiscussionReplyReactionSummary(
            reactions.Count(r => r.Reaction == DiscussionReplyReactionType.Like),
            reactions.Count(r => r.Reaction == DiscussionReplyReactionType.Dislike),
            reactions.FirstOrDefault(r => r.UserId == userId)?.Reaction);
    }
}
