using backend.main.application.security.authorization;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.exceptions.http;

namespace backend.main.features.clubs.discussions.replies;

public sealed class ClubDiscussionReplyService : IClubDiscussionReplyService
{
    private readonly IClubDiscussionReplyRepository _replyRepository;
    private readonly IClubDiscussionRepository _discussionRepository;
    private readonly IResourceAuthorizer _authorizer;
    private readonly IUserRepository _userRepository;

    public const string ReadAnonymousMessage = "Authentication is required to view discussions for a private club.";
    public const string ReadForbiddenMessage = "You must be a member of this club to view its discussions.";
    public const string ParticipateForbiddenMessage = "You must be a member of this club to participate in its discussions.";

    public ClubDiscussionReplyService(
        IClubDiscussionReplyRepository replyRepository,
        IClubDiscussionRepository discussionRepository,
        IResourceAuthorizer authorizer,
        IUserRepository userRepository)
    {
        _replyRepository = replyRepository;
        _discussionRepository = discussionRepository;
        _authorizer = authorizer;
        _userRepository = userRepository;
    }

    public Task EnsureCanReadClubAsync(int clubId, int? userId, string? userRole) =>
        _authorizer.EnsureAsync(
            new ResourceActor(userId, userRole),
            Permissions.ClubRead,
            new ClubResource(clubId),
            new PermissionMessages(ReadForbiddenMessage, ReadAnonymousMessage));

    public async Task EnsureCanReadDiscussionAsync(
        int clubId, int discussionId, int? userId, string? userRole)
    {
        await EnsureCanReadClubAsync(clubId, userId, userRole);
        await GetDiscussionAsync(clubId, discussionId);
    }

    public async Task<DiscussionReplyPage> GetPageAsync(
        int clubId, int discussionId, int? parentReplyId, DiscussionReplySort sort,
        string? cursor, int pageSize, int? currentUserId, string? currentUserRole)
    {
        if (!Enum.IsDefined(sort))
            throw new BadRequestException("Reply sort must be Newest or Oldest.");
        await EnsureCanReadClubAsync(clubId, currentUserId, currentUserRole);
        await GetDiscussionAsync(clubId, discussionId);

        if (parentReplyId.HasValue)
            await GetReplyAsync(discussionId, parentReplyId.Value);

        var decodedCursor = DiscussionReplyCursorCodec.Decode(cursor);
        var (items, hasMore) = await _replyRepository.GetPageAsync(
            discussionId, parentReplyId, sort, decodedCursor, pageSize);
        var totalCount = await _replyRepository.CountByParentAsync(discussionId, parentReplyId);
        var views = await BuildViewsAsync(items, currentUserId);
        var nextCursor = hasMore && items.Count > 0
            ? DiscussionReplyCursorCodec.Encode(items[^1])
            : null;
        return new DiscussionReplyPage(views, totalCount, nextCursor, hasMore);
    }

    public async Task<DiscussionReplyView> CreateAsync(
        int clubId, int discussionId, int? parentReplyId, int userId, string? userRole, string content)
    {
        await EnsureCanParticipateAsync(clubId, userId, userRole);
        await GetDiscussionAsync(clubId, discussionId);
        var normalized = NormalizeContent(content);

        if (parentReplyId.HasValue)
        {
            var parent = await GetReplyAsync(discussionId, parentReplyId.Value);
            if (parent.IsDeleted)
                throw new BadRequestException("Deleted replies cannot receive new replies.");
        }

        var reply = await _replyRepository.CreateAsync(new ClubDiscussionReply
        {
            DiscussionId = discussionId,
            ParentReplyId = parentReplyId,
            UserId = userId,
            Content = normalized
        });
        return await BuildViewAsync(reply, userId);
    }

    public async Task<DiscussionReplyView> UpdateAsync(
        int clubId, int discussionId, int replyId, int userId, string? userRole, string content)
    {
        await EnsureCanParticipateAsync(clubId, userId, userRole);
        await GetDiscussionAsync(clubId, discussionId);
        var reply = await GetReplyAsync(discussionId, replyId);
        await EnsureAuthorAsync(reply, userId, "update");
        if (reply.IsDeleted)
            throw new BadRequestException("Deleted replies cannot be edited.");

        var updated = await _replyRepository.UpdateAsync(replyId, NormalizeContent(content))
            ?? throw new ResourceNotFoundException($"Reply with ID {replyId} was not found.");
        return await BuildViewAsync(updated, userId);
    }

    public async Task<DiscussionReplyView> DeleteAsync(
        int clubId, int discussionId, int replyId, int userId, string? userRole)
    {
        await EnsureCanParticipateAsync(clubId, userId, userRole);
        await GetDiscussionAsync(clubId, discussionId);
        var reply = await GetReplyAsync(discussionId, replyId);
        await EnsureAuthorAsync(reply, userId, "delete");
        var deleted = await _replyRepository.SoftDeleteAsync(replyId)
            ?? throw new ResourceNotFoundException($"Reply with ID {replyId} was not found.");
        return await BuildViewAsync(deleted, userId);
    }

    public async Task<DiscussionReplyReactionSummary> SetReactionAsync(
        int clubId, int discussionId, int replyId, int userId, string? userRole,
        DiscussionReplyReactionType reaction)
    {
        if (!Enum.IsDefined(reaction))
            throw new BadRequestException("Reaction must be Like or Dislike.");
        await EnsureCanParticipateAsync(clubId, userId, userRole);
        await GetDiscussionAsync(clubId, discussionId);
        var reply = await GetReplyAsync(discussionId, replyId);
        if (reply.IsDeleted)
            throw new BadRequestException("Deleted replies cannot be reacted to.");
        return await _replyRepository.SetReactionAsync(replyId, userId, reaction);
    }

    public async Task<DiscussionReplyReactionSummary> ClearReactionAsync(
        int clubId, int discussionId, int replyId, int userId, string? userRole)
    {
        await EnsureCanParticipateAsync(clubId, userId, userRole);
        await GetDiscussionAsync(clubId, discussionId);
        var reply = await GetReplyAsync(discussionId, replyId);
        if (reply.IsDeleted)
            throw new BadRequestException("Deleted replies cannot be reacted to.");
        return await _replyRepository.ClearReactionAsync(replyId, userId);
    }

    private Task EnsureCanParticipateAsync(int clubId, int userId, string? userRole) =>
        _authorizer.EnsureAsync(
            ResourceActor.User(userId, userRole),
            Permissions.ClubParticipate,
            new ClubResource(clubId),
            new PermissionMessages(ParticipateForbiddenMessage));

    private async Task<ClubDiscussion> GetDiscussionAsync(int clubId, int discussionId)
    {
        var discussion = await _discussionRepository.GetByIdAsync(discussionId)
            ?? throw new ResourceNotFoundException($"Discussion with ID {discussionId} was not found.");
        if (discussion.ClubId != clubId)
            throw new ResourceNotFoundException($"Discussion with ID {discussionId} was not found.");
        return discussion;
    }

    private async Task<ClubDiscussionReply> GetReplyAsync(int discussionId, int replyId)
    {
        var reply = await _replyRepository.GetByIdAsync(replyId)
            ?? throw new ResourceNotFoundException($"Reply with ID {replyId} was not found.");
        if (reply.DiscussionId != discussionId)
            throw new ResourceNotFoundException($"Reply with ID {replyId} was not found.");
        return reply;
    }

    private Task EnsureAuthorAsync(ClubDiscussionReply reply, int userId, string action) =>
        _authorizer.EnsureAsync(
            ResourceActor.User(userId, null),
            Permissions.ContentAuthor,
            new AuthoredContentResource("Reply", reply.Id, reply.UserId),
            new PermissionMessages($"You are not allowed to {action} this reply."));

    private static string NormalizeContent(string content)
    {
        var normalized = content?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            throw new BadRequestException("Reply content is required.");
        if (normalized.Length > 1000)
            throw new BadRequestException("Reply content cannot exceed 1000 characters.");
        return normalized;
    }

    private async Task<IReadOnlyList<DiscussionReplyView>> BuildViewsAsync(
        IReadOnlyList<ClubDiscussionReply> replies, int? currentUserId)
    {
        if (replies.Count == 0)
            return [];
        var ids = replies.Select(r => r.Id).ToList();
        var childCounts = await _replyRepository.GetDirectReplyCountsAsync(ids);
        var reactions = await _replyRepository.GetReactionSummariesAsync(ids, currentUserId);
        var userIds = replies.Where(r => !r.IsDeleted).Select(r => r.UserId).Distinct().ToList();
        var authors = userIds.Count == 0
            ? new Dictionary<int, UserListRecord>()
            : (await _userRepository.GetByIdsAsync(userIds)).ToDictionary(u => u.Id);

        return replies.Select(r =>
        {
            var summary = reactions.GetValueOrDefault(r.Id) ?? new(0, 0, null);
            return new DiscussionReplyView(
                r,
                r.IsDeleted ? null : authors.GetValueOrDefault(r.UserId),
                r.IsDeleted ? 0 : summary.LikeCount,
                r.IsDeleted ? 0 : summary.DislikeCount,
                r.IsDeleted ? null : summary.CurrentUserReaction,
                childCounts.GetValueOrDefault(r.Id));
        }).ToList();
    }

    private async Task<DiscussionReplyView> BuildViewAsync(ClubDiscussionReply reply, int? currentUserId) =>
        (await BuildViewsAsync([reply], currentUserId))[0];
}
