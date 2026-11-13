using backend.main.application.security.authorization;
using backend.main.features.cache;
using backend.main.features.clubs.moderation;
using backend.main.features.clubs.posts.comments;
using backend.main.features.clubs.posts.search;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.infrastructure.database.core;
using backend.main.infrastructure.elasticsearch;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.clubs.posts
{
    public class ClubPostService : IClubPostService
    {
        private static readonly TimeSpan PostTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan NotFoundTTL = TimeSpan.FromSeconds(15);

        private static string PostCacheKey(int postId) => $"post:{postId}";

        private readonly AppDatabaseContext _db;
        private readonly IClubPostRepository _postRepository;
        private readonly IPostCommentRepository _commentRepository;
        private readonly IResourceAuthorizer _authorizer;
        private readonly IClubPostSearchService _searchService;
        private readonly IClubPostSearchOutboxWriter _outboxWriter;
        private readonly IUserRepository _userRepository;
        private readonly IRefreshAheadCache _cache;
        private readonly IClubModerationLog _moderationLog;

        public ClubPostService(
            AppDatabaseContext db,
            IClubPostRepository postRepository,
            IPostCommentRepository commentRepository,
            IResourceAuthorizer authorizer,
            IClubPostSearchService searchService,
            IClubPostSearchOutboxWriter outboxWriter,
            IUserRepository userRepository,
            IRefreshAheadCache cache,
            IClubModerationLog moderationLog)
        {
            _db = db;
            _postRepository = postRepository;
            _commentRepository = commentRepository;
            _authorizer = authorizer;
            _searchService = searchService;
            _outboxWriter = outboxWriter;
            _userRepository = userRepository;
            _cache = cache;
            _moderationLog = moderationLog;
        }

        public const string ReadAnonymousMessage = "Authentication is required to view posts for a private club.";
        public const string ReadForbiddenMessage = "You must be a member of this club to view its posts.";
        public const string CreateForbiddenMessage = "You are not allowed to create posts for this club.";
        public const string UpdateForbiddenMessage = "You are not allowed to update this post.";
        public const string DeleteForbiddenMessage = "You are not allowed to delete this post.";
        public const string PinForbiddenMessage = "Only the club's managers and owner can pin or unpin posts.";

        public async Task<ClubPost> CreateAsync(int clubId, int userId, string userRole, string title, string content, PostType postType, bool isPinned)
        {
            await EnsureStaffAsync(clubId, userId, userRole, CreateForbiddenMessage);
            if (isPinned)
                await EnsureCanPinAsync(clubId, userId, userRole);

            // The entity is built inside the retryable unit on purpose. An execution-strategy
            // retry re-runs this delegate, and reusing an instance from a previous attempt
            // would re-Add an entity the change tracker already knows, inserting a duplicate
            // row and staging the outbox entry twice.
            return await ExecuteInTransactionAsync(async () =>
            {
                var created = await _postRepository.CreateAsync(new ClubPost
                {
                    ClubId = clubId,
                    UserId = userId,
                    Title = title,
                    Content = content,
                    PostType = postType,
                    IsPinned = isPinned
                });

                _outboxWriter.StageUpsert(created);
                await _db.SaveChangesAsync();
                return created;
            });
        }

        public async Task<(ClubPost Post, UserListRecord? Author)> GetByIdAsync(
            int clubId, int postId, int? requestingUserId, string? requestingUserRole)
        {
            await EnsureCanReadAsync(clubId, requestingUserId, requestingUserRole);

            var post = await _cache.GetOrSetAsync(
                PostCacheKey(postId),
                () => _postRepository.GetByIdAsync(postId),
                PostTTL,
                nullSentinelTtl: NotFoundTTL);

            if (post == null || post.ClubId != clubId)
                throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            await PopulateCommentCountsAsync([post]);
            var users = await _userRepository.GetByIdsAsync([post.UserId]);
            return (post, users.Count > 0 ? users[0] : null);
        }

        public async Task<(List<ClubPost> Items, int TotalCount, string Source, Dictionary<int, UserListRecord> Authors)> GetByClubIdAsync(
            int clubId, int? requestingUserId, string? requestingUserRole, string? search, PostSortBy sortBy, int page, int pageSize)
        {
            await EnsureCanReadAsync(clubId, requestingUserId, requestingUserRole);

            if (!string.IsNullOrWhiteSpace(search))
            {
                try
                {
                    var (ids, total) = await _searchService.SearchByClubAsync(clubId, search, sortBy, page, pageSize);
                    var posts = ids.Count > 0
                        ? await _postRepository.GetByIdsAsync(ids)
                        : [];
                    if (posts.Count > 0)
                        await _postRepository.IncrementViewCountAsync(posts.Select(p => p.Id));
                    await PopulateCommentCountsAsync(posts);
                    return (posts, total, ResponseSource.Elasticsearch, await FetchAuthorLookupAsync(posts));
                }
                catch (ElasticsearchDisabledException ex)
                {
                    Logger.Info($"Elasticsearch disabled for club post search. Falling back to database LIKE search. {ex.Message}");
                }
                catch (ElasticsearchUnavailableException ex)
                {
                    Logger.Warn(ex, "Elasticsearch temporarily unavailable. Falling back to database LIKE search.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Club post Elasticsearch search failed with a non-fallback error: {ex}");
                    throw;
                }
            }

            var resultPosts = await _postRepository.GetByClubIdAsync(clubId, search, sortBy, page, pageSize);
            var totalCount = await _postRepository.CountByClubIdAsync(clubId, search);
            if (resultPosts.Count > 0)
                await _postRepository.IncrementViewCountAsync(resultPosts.Select(p => p.Id));
            await PopulateCommentCountsAsync(resultPosts);

            return (resultPosts, totalCount, ResponseSource.Database, await FetchAuthorLookupAsync(resultPosts));
        }

        public async Task<ClubPost> UpdateAsync(
            int clubId, int postId, int userId, string userRole, string title, string content, PostType postType, bool isPinned,
            string? moderationReason = null)
        {
            var existing = await _postRepository.GetByIdAsync(postId)
                ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (existing.ClubId != clubId)
                throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            await EnsureStaffAsync(clubId, userId, userRole, UpdateForbiddenMessage);
            var resource = ContentOf(existing);
            var decision = await _authorizer.AuthorizeContentAsync(
                ResourceActor.User(userId, userRole),
                Permissions.PostManage,
                resource,
                new PermissionMessages(UpdateForbiddenMessage));

            var pinChanged = existing.IsPinned != isPinned;
            if (pinChanged)
                await EnsureCanPinAsync(clubId, userId, userRole);

            var reason = ClubModeration.OptionalReason(moderationReason);
            var contentChanged = existing.Title != title || existing.Content != content || existing.PostType != postType;
            var action = contentChanged || !pinChanged
                ? ClubModerationActionTypes.Edit
                : isPinned ? ClubModerationActionTypes.Pin : ClubModerationActionTypes.Unpin;
            var snapshot = existing.Title;

            var updated = await ExecuteInTransactionAsync(async () =>
            {
                if (decision.IsModeration)
                    _moderationLog.Stage(ClubModeration.Entry(resource, userId, decision, action, snapshot, reason));

                var post = await _postRepository.UpdateAsync(postId, new ClubPost
                {
                    Title = title,
                    Content = content,
                    PostType = postType,
                    IsPinned = isPinned
                }) ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

                _outboxWriter.StageUpsert(post);
                await _db.SaveChangesAsync();
                return post;
            });

            await _cache.RemoveAsync(PostCacheKey(postId));

            await PopulateCommentCountsAsync([updated]);

            return updated;
        }

        public async Task DeleteAsync(int clubId, int postId, int userId, string userRole, string? reason = null)
        {
            var post = await _postRepository.GetByIdAsync(postId)
                ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (post.ClubId != clubId)
                throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            await EnsureStaffAsync(clubId, userId, userRole, DeleteForbiddenMessage);
            var resource = ContentOf(post);
            var decision = await _authorizer.AuthorizeContentAsync(
                ResourceActor.User(userId, userRole),
                Permissions.PostManage,
                resource,
                new PermissionMessages(DeleteForbiddenMessage));
            var moderationReason = decision.IsModeration ? ClubModeration.RequireReason(reason) : null;

            await ExecuteInTransactionAsync(async () =>
            {
                if (decision.IsModeration)
                {
                    _moderationLog.Stage(ClubModeration.Entry(
                        resource, userId, decision, ClubModerationActionTypes.Remove, post.Title, moderationReason));
                }

                await _postRepository.DeleteAsync(postId);
                _outboxWriter.StageDelete(postId);
                await _db.SaveChangesAsync();
                return true;
            });

            await _cache.RemoveAsync(PostCacheKey(postId));
        }

        private Task EnsureCanReadAsync(int clubId, int? userId, string? userRole) =>
            _authorizer.EnsureAsync(
                new ResourceActor(userId, userRole),
                Permissions.ClubRead,
                new ClubResource(clubId),
                new PermissionMessages(ReadForbiddenMessage, ReadAnonymousMessage));

        // Any staff member may post. Changing a post also needs post.manage: its author while still
        // staff, or a manager, the owner or an admin. Pinning is for managers, the owner and admins.
        private Task EnsureStaffAsync(int clubId, int userId, string userRole, string message) =>
            _authorizer.EnsureAsync(
                ResourceActor.User(userId, userRole),
                Permissions.ClubStaffAccess,
                new ClubResource(clubId),
                new PermissionMessages(message));

        private Task EnsureCanPinAsync(int clubId, int userId, string userRole) =>
            _authorizer.EnsureAsync(
                ResourceActor.User(userId, userRole),
                Permissions.ClubManage,
                new ClubResource(clubId),
                new PermissionMessages(PinForbiddenMessage));

        private static ClubContentResource ContentOf(ClubPost post) =>
            new(post.ClubId, ClubModerationContentTypes.Post, post.Id, post.UserId);

        public async Task<(List<ClubPost> Items, int TotalCount, string Source)> GetAllAdminAsync(
            string? search, PostSortBy sortBy, int page, int pageSize)
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                try
                {
                    var (ids, total) = await _searchService.SearchAllAsync(search, sortBy, page, pageSize);
                    var posts = ids.Count > 0
                        ? await _postRepository.GetByIdsAsync(ids)
                        : [];
                    await PopulateCommentCountsAsync(posts);
                    return (posts, total, ResponseSource.Elasticsearch);
                }
                catch (ElasticsearchDisabledException ex)
                {
                    Logger.Info($"Elasticsearch disabled for admin club post search. Falling back to database LIKE search. {ex.Message}");
                }
                catch (ElasticsearchUnavailableException ex)
                {
                    Logger.Warn(ex, "Elasticsearch temporarily unavailable for admin club post search. Falling back to database LIKE search.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Admin club post Elasticsearch search failed with a non-fallback error: {ex}");
                    throw;
                }
            }

            var items = await _postRepository.GetAllAsync(search, sortBy, page, pageSize);
            var totalCount = await _postRepository.CountAllAsync(search);
            await PopulateCommentCountsAsync(items);

            return (items, totalCount, ResponseSource.Database);
        }

        /// <summary>
        /// Runs a unit of work in a transaction through the context's execution strategy.
        /// </summary>
        /// <remarks>
        /// The context enables retry-on-failure, and that strategy rejects a user-initiated
        /// transaction unless the whole unit runs through it. Mirrors ClubService's helper.
        /// </remarks>
        private async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var result = await action();
                    await transaction.CommitAsync();
                    return result;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }

        private async Task<Dictionary<int, UserListRecord>> FetchAuthorLookupAsync(List<ClubPost> posts)
        {
            var userIds = posts.Select(p => p.UserId).Distinct().ToList();
            if (userIds.Count == 0)
                return [];
            var users = await _userRepository.GetByIdsAsync(userIds);
            return users.ToDictionary(u => u.Id);
        }

        private async Task PopulateCommentCountsAsync(IReadOnlyList<ClubPost> posts)
        {
            if (posts.Count == 0)
                return;
            var counts = await _commentRepository.CountByPostIdsAsync(posts.Select(post => post.Id));
            foreach (var post in posts)
                post.CommentCount = counts.GetValueOrDefault(post.Id);
        }
    }
}
