using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.clubs.posts.comments;

public sealed class PostCommentRepository : IPostCommentRepository
{
    private readonly AppDatabaseContext _context;

    public PostCommentRepository(AppDatabaseContext context) => _context = context;

    public async Task<PostComment> CreateAsync(PostComment comment)
    {
        _context.PostComments.Add(comment);
        await _context.SaveChangesAsync();
        return comment;
    }

    public Task<PostComment?> GetByIdAsync(int id) =>
        _context.PostComments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);

    public async Task<(List<PostComment> Items, bool HasMore)> GetPageAsync(
        int postId, int? parentCommentId, PostCommentSort sort,
        PostCommentCursor? cursor, int pageSize)
    {
        var query = _context.PostComments.AsNoTracking()
            .Where(c => c.PostId == postId && c.ParentCommentId == parentCommentId);

        if (cursor is not null)
        {
            query = sort == PostCommentSort.Newest
                ? query.Where(c => c.CreatedAt < cursor.CreatedAt ||
                    (c.CreatedAt == cursor.CreatedAt && c.Id < cursor.Id))
                : query.Where(c => c.CreatedAt > cursor.CreatedAt ||
                    (c.CreatedAt == cursor.CreatedAt && c.Id > cursor.Id));
        }

        query = sort == PostCommentSort.Newest
            ? query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            : query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id);

        var items = await query.Take(pageSize + 1).ToListAsync();
        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);
        return (items, hasMore);
    }

    public Task<int> CountByParentAsync(int postId, int? parentCommentId) =>
        _context.PostComments.AsNoTracking()
            .CountAsync(c => c.PostId == postId && c.ParentCommentId == parentCommentId);

    public async Task<Dictionary<int, int>> CountByPostIdsAsync(IEnumerable<int> postIds)
    {
        var ids = postIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];
        return await _context.PostComments.AsNoTracking()
            .Where(c => ids.Contains(c.PostId))
            .GroupBy(c => c.PostId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count);
    }

    public async Task<Dictionary<int, int>> GetDirectReplyCountsAsync(IEnumerable<int> commentIds)
    {
        var ids = commentIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];
        return await _context.PostComments.AsNoTracking()
            .Where(c => c.ParentCommentId.HasValue && ids.Contains(c.ParentCommentId.Value))
            .GroupBy(c => c.ParentCommentId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count);
    }

    public async Task<Dictionary<int, PostCommentReactionSummary>> GetReactionSummariesAsync(
        IEnumerable<int> commentIds, int? currentUserId)
    {
        var ids = commentIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var counts = await _context.PostCommentReactions.AsNoTracking()
            .Where(r => ids.Contains(r.CommentId))
            .GroupBy(r => r.CommentId)
            .Select(group => new
            {
                CommentId = group.Key,
                Likes = group.Count(r => r.Reaction == PostCommentReactionType.Like),
                Dislikes = group.Count(r => r.Reaction == PostCommentReactionType.Dislike)
            })
            .ToDictionaryAsync(item => item.CommentId);

        Dictionary<int, PostCommentReactionType> mine = [];
        if (currentUserId.HasValue)
        {
            mine = await _context.PostCommentReactions.AsNoTracking()
                .Where(r => r.UserId == currentUserId.Value && ids.Contains(r.CommentId))
                .ToDictionaryAsync(r => r.CommentId, r => r.Reaction);
        }

        return ids.ToDictionary(
            id => id,
            id => new PostCommentReactionSummary(
                counts.GetValueOrDefault(id)?.Likes ?? 0,
                counts.GetValueOrDefault(id)?.Dislikes ?? 0,
                mine.TryGetValue(id, out var reaction) ? reaction : null));
    }

    public async Task<PostComment?> UpdateAsync(int id, string content)
    {
        var comment = await _context.PostComments.FindAsync(id);
        if (comment is null)
            return null;
        comment.Content = content;
        comment.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return comment;
    }

    public async Task<PostComment?> SoftDeleteAsync(int id)
    {
        // The context is configured with EnableRetryOnFailure, and that execution strategy
        // refuses a user-initiated transaction unless the whole unit runs through it.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<PostComment?>(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var comment = await _context.PostComments.FindAsync(id);
            if (comment is null)
                return null;

            if (!comment.IsDeleted)
            {
                comment.IsDeleted = true;
                comment.DeletedAt = DateTime.UtcNow;
                comment.UpdatedAt = comment.DeletedAt.Value;
                comment.Content = string.Empty;
            }

            // Outside the guard on purpose: a retried attempt finds the entity already
            // mutated in the change tracker, and this delete is idempotent either way.
            await _context.PostCommentReactions
                .Where(reaction => reaction.CommentId == id)
                .ExecuteDeleteAsync();
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();
            return comment;
        });
    }

    public async Task<PostCommentReactionSummary> SetReactionAsync(
        int commentId, int userId, PostCommentReactionType reaction)
    {
        var existing = await _context.PostCommentReactions
            .SingleOrDefaultAsync(r => r.CommentId == commentId && r.UserId == userId);
        PostCommentReaction? created = null;
        if (existing is null)
        {
            created = new PostCommentReaction
            {
                CommentId = commentId,
                UserId = userId,
                Reaction = reaction
            };
            _context.PostCommentReactions.Add(created);
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
            _context.Entry(created).State = EntityState.Detached;
            existing = await _context.PostCommentReactions
                .SingleAsync(r => r.CommentId == commentId && r.UserId == userId);
            if (existing.Reaction != reaction)
            {
                existing.Reaction = reaction;
                existing.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
        return await GetReactionSummaryAsync(commentId, userId);
    }

    public async Task<PostCommentReactionSummary> ClearReactionAsync(int commentId, int userId)
    {
        var existing = await _context.PostCommentReactions
            .SingleOrDefaultAsync(r => r.CommentId == commentId && r.UserId == userId);
        if (existing is not null)
        {
            _context.PostCommentReactions.Remove(existing);
            await _context.SaveChangesAsync();
        }
        return await GetReactionSummaryAsync(commentId, userId);
    }

    private async Task<PostCommentReactionSummary> GetReactionSummaryAsync(int commentId, int userId)
    {
        var reactions = await _context.PostCommentReactions.AsNoTracking()
            .Where(r => r.CommentId == commentId)
            .ToListAsync();
        return new PostCommentReactionSummary(
            reactions.Count(r => r.Reaction == PostCommentReactionType.Like),
            reactions.Count(r => r.Reaction == PostCommentReactionType.Dislike),
            reactions.FirstOrDefault(r => r.UserId == userId)?.Reaction);
    }
}
