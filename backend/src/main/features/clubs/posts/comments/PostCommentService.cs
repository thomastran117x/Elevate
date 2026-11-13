using backend.main.application.security.authorization;
using backend.main.features.clubs.moderation;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.exceptions.http;

namespace backend.main.features.clubs.posts.comments;

public sealed class PostCommentService : IPostCommentService
{
    private readonly IPostCommentRepository _commentRepository;
    private readonly IClubPostRepository _postRepository;
    private readonly IResourceAuthorizer _authorizer;
    private readonly IUserRepository _userRepository;
    private readonly IClubModerationLog _moderationLog;

    public const string ReadAnonymousMessage = "Authentication is required to view comments for a private club.";
    public const string ReadForbiddenMessage = "You must be a member of this club to view its comments.";
    public const string ParticipateForbiddenMessage = "You must be a member of this club to participate in its comments.";

    public PostCommentService(
        IPostCommentRepository commentRepository,
        IClubPostRepository postRepository,
        IResourceAuthorizer authorizer,
        IUserRepository userRepository,
        IClubModerationLog moderationLog)
    {
        _commentRepository = commentRepository;
        _postRepository = postRepository;
        _authorizer = authorizer;
        _userRepository = userRepository;
        _moderationLog = moderationLog;
    }

    public async Task EnsureCanReadPostAsync(int clubId, int postId, int? userId, string? userRole)
    {
        // The club's privacy is checked before the post is looked up, so the ids of a private
        // club's posts cannot be probed through the not-found response.
        await _authorizer.EnsureAsync(
            new ResourceActor(userId, userRole),
            Permissions.ClubRead,
            new ClubResource(clubId),
            new PermissionMessages(ReadForbiddenMessage, ReadAnonymousMessage));
        await GetPostAsync(clubId, postId);
    }

    public async Task<PostCommentPage> GetPageAsync(
        int clubId, int postId, int? parentCommentId, PostCommentSort sort,
        string? cursor, int pageSize, int? currentUserId, string? currentUserRole)
    {
        if (!Enum.IsDefined(sort))
            throw new BadRequestException("Comment sort must be Newest or Oldest.");
        await EnsureCanReadPostAsync(clubId, postId, currentUserId, currentUserRole);

        if (parentCommentId.HasValue)
            await GetCommentAsync(postId, parentCommentId.Value);

        var decodedCursor = PostCommentCursorCodec.Decode(cursor);
        var (items, hasMore) = await _commentRepository.GetPageAsync(
            postId, parentCommentId, sort, decodedCursor, pageSize);
        var totalCount = await _commentRepository.CountByParentAsync(postId, parentCommentId);
        var views = await BuildViewsAsync(items, currentUserId);
        var nextCursor = hasMore && items.Count > 0
            ? PostCommentCursorCodec.Encode(items[^1])
            : null;
        return new PostCommentPage(views, totalCount, nextCursor, hasMore);
    }

    public async Task<PostCommentView> CreateAsync(
        int clubId, int postId, int? parentCommentId, int userId, string? userRole, string content)
    {
        await EnsureCanParticipateAsync(clubId, postId, userId, userRole);
        var normalized = NormalizeContent(content);

        if (parentCommentId.HasValue)
        {
            var parent = await GetCommentAsync(postId, parentCommentId.Value);
            if (parent.IsDeleted)
                throw new BadRequestException("Deleted comments cannot receive new replies.");
        }

        var comment = await _commentRepository.CreateAsync(new PostComment
        {
            PostId = postId,
            ParentCommentId = parentCommentId,
            UserId = userId,
            Content = normalized
        });
        return await BuildViewAsync(comment, userId);
    }

    public async Task<PostCommentView> UpdateAsync(
        int clubId, int postId, int commentId, int userId, string? userRole, string content)
    {
        await EnsureCanParticipateAsync(clubId, postId, userId, userRole);
        var comment = await GetCommentAsync(postId, commentId);
        await EnsureAuthorAsync(comment, userId, "update");
        if (comment.IsDeleted)
            throw new BadRequestException("Deleted comments cannot be edited.");

        var updated = await _commentRepository.UpdateAsync(commentId, NormalizeContent(content))
            ?? throw new ResourceNotFoundException($"Comment with ID {commentId} was not found.");
        return await BuildViewAsync(updated, userId);
    }

    public async Task<PostCommentView> DeleteAsync(
        int clubId, int postId, int commentId, int userId, string? userRole, string? reason = null)
    {
        await EnsureCanParticipateAsync(clubId, postId, userId, userRole);
        var comment = await GetCommentAsync(postId, commentId);

        // The author, staff who outrank the author in this club, or an admin.
        var resource = new ClubContentResource(clubId, ClubModerationContentTypes.Comment, comment.Id, comment.UserId);
        var decision = await _authorizer.AuthorizeContentAsync(
            ResourceActor.User(userId, userRole),
            Permissions.ContentRemove,
            resource,
            new PermissionMessages("You are not allowed to delete this comment."));

        // Deleting an already deleted comment changes nothing, so there is nothing to record.
        if (decision.IsModeration && !comment.IsDeleted)
        {
            var moderationReason = ClubModeration.RequireReason(reason);
            // Staged before the soft delete so it commits in the repository's transaction.
            _moderationLog.Stage(ClubModeration.Entry(
                resource, userId, decision, ClubModerationActionTypes.Remove, comment.Content, moderationReason));
        }

        var deleted = await _commentRepository.SoftDeleteAsync(commentId)
            ?? throw new ResourceNotFoundException($"Comment with ID {commentId} was not found.");
        return await BuildViewAsync(deleted, userId);
    }

    public async Task<PostCommentReactionSummary> SetReactionAsync(
        int clubId, int postId, int commentId, int userId, string? userRole,
        PostCommentReactionType reaction)
    {
        if (!Enum.IsDefined(reaction))
            throw new BadRequestException("Reaction must be Like or Dislike.");
        await EnsureCanParticipateAsync(clubId, postId, userId, userRole);
        var comment = await GetCommentAsync(postId, commentId);
        if (comment.IsDeleted)
            throw new BadRequestException("Deleted comments cannot be reacted to.");
        return await _commentRepository.SetReactionAsync(commentId, userId, reaction);
    }

    public async Task<PostCommentReactionSummary> ClearReactionAsync(
        int clubId, int postId, int commentId, int userId, string? userRole)
    {
        await EnsureCanParticipateAsync(clubId, postId, userId, userRole);
        var comment = await GetCommentAsync(postId, commentId);
        if (comment.IsDeleted)
            throw new BadRequestException("Deleted comments cannot be reacted to.");
        return await _commentRepository.ClearReactionAsync(commentId, userId);
    }

    private async Task EnsureCanParticipateAsync(int clubId, int postId, int userId, string? userRole)
    {
        await _authorizer.EnsureAsync(
            ResourceActor.User(userId, userRole),
            Permissions.ClubParticipate,
            new ClubResource(clubId),
            new PermissionMessages(ParticipateForbiddenMessage));
        await GetPostAsync(clubId, postId);
    }

    private async Task<ClubPost> GetPostAsync(int clubId, int postId)
    {
        var post = await _postRepository.GetByIdAsync(postId)
            ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");
        if (post.ClubId != clubId)
            throw new ResourceNotFoundException($"Post with ID {postId} was not found.");
        return post;
    }

    private async Task<PostComment> GetCommentAsync(int postId, int commentId)
    {
        var comment = await _commentRepository.GetByIdAsync(commentId)
            ?? throw new ResourceNotFoundException($"Comment with ID {commentId} was not found.");
        if (comment.PostId != postId)
            throw new ResourceNotFoundException($"Comment with ID {commentId} was not found.");
        return comment;
    }

    private Task EnsureAuthorAsync(PostComment comment, int userId, string action) =>
        _authorizer.EnsureAsync(
            ResourceActor.User(userId, null),
            Permissions.ContentAuthor,
            new AuthoredContentResource("Comment", comment.Id, comment.UserId),
            new PermissionMessages($"You are not allowed to {action} this comment."));

    private static string NormalizeContent(string content)
    {
        var normalized = content?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            throw new BadRequestException("Comment content is required.");
        if (normalized.Length > 1000)
            throw new BadRequestException("Comment content cannot exceed 1000 characters.");
        return normalized;
    }

    private async Task<IReadOnlyList<PostCommentView>> BuildViewsAsync(
        IReadOnlyList<PostComment> comments, int? currentUserId)
    {
        if (comments.Count == 0)
            return [];
        var ids = comments.Select(comment => comment.Id).ToList();
        var childCounts = await _commentRepository.GetDirectReplyCountsAsync(ids);
        var reactions = await _commentRepository.GetReactionSummariesAsync(ids, currentUserId);
        var userIds = comments.Where(comment => !comment.IsDeleted)
            .Select(comment => comment.UserId).Distinct().ToList();
        var authors = userIds.Count == 0
            ? new Dictionary<int, UserListRecord>()
            : (await _userRepository.GetByIdsAsync(userIds)).ToDictionary(user => user.Id);

        return comments.Select(comment =>
        {
            var summary = reactions.GetValueOrDefault(comment.Id) ?? new(0, 0, null);
            return new PostCommentView(
                comment,
                comment.IsDeleted ? null : authors.GetValueOrDefault(comment.UserId),
                comment.IsDeleted ? 0 : summary.LikeCount,
                comment.IsDeleted ? 0 : summary.DislikeCount,
                comment.IsDeleted ? null : summary.CurrentUserReaction,
                childCounts.GetValueOrDefault(comment.Id));
        }).ToList();
    }

    private async Task<PostCommentView> BuildViewAsync(PostComment comment, int? currentUserId) =>
        (await BuildViewsAsync([comment], currentUserId))[0];
}
