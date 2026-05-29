using backend.main.features.cache;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.posts.search;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.infrastructure.database.core;
using backend.main.infrastructure.elasticsearch;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;

namespace backend.main.features.clubs.posts
{
    public class ClubPostService : IClubPostService
    {
        private static readonly TimeSpan PostTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan NotFoundTTL = TimeSpan.FromSeconds(15);

        private static string PostCacheKey(int postId) => $"post:{postId}";

        private readonly AppDatabaseContext _db;
        private readonly IClubPostRepository _postRepository;
        private readonly IClubService _clubService;
        private readonly IFollowRepository _followRepository;
        private readonly IClubPostSearchService _searchService;
        private readonly IClubPostSearchOutboxWriter _outboxWriter;
        private readonly IUserRepository _userRepository;
        private readonly IRefreshAheadCache _cache;

        public ClubPostService(
            AppDatabaseContext db,
            IClubPostRepository postRepository,
            IClubService clubService,
            IFollowRepository followRepository,
            IClubPostSearchService searchService,
            IClubPostSearchOutboxWriter outboxWriter,
            IUserRepository userRepository,
            IRefreshAheadCache cache)
        {
            _db = db;
            _postRepository = postRepository;
            _clubService = clubService;
            _followRepository = followRepository;
            _searchService = searchService;
            _outboxWriter = outboxWriter;
            _userRepository = userRepository;
            _cache = cache;
        }

        public async Task<ClubPost> CreateAsync(int clubId, int userId, string userRole, string title, string content, PostType postType, bool isPinned)
        {
            var club = await _clubService.GetClub(clubId);

            if (!await _clubService.CanManageClubPostsAsync(clubId, userId, userRole))
                throw new ForbiddenException("You are not allowed to create posts for this club.");

            var post = new ClubPost
            {
                ClubId = clubId,
                UserId = userId,
                Title = title,
                Content = content,
                PostType = postType,
                IsPinned = isPinned
            };

            await using var transaction = await _db.Database.BeginTransactionAsync();

            post = await _postRepository.CreateAsync(post);
            _outboxWriter.StageUpsert(post);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return post;
        }

        public async Task<(ClubPost Post, UserListRecord? Author)> GetByIdAsync(
            int clubId, int postId, int? requestingUserId, string? requestingUserRole)
        {
            var club = await _clubService.GetClub(clubId);

            if (club.isPrivate)
            {
                if (requestingUserId == null)
                    throw new UnauthorizedException("Authentication is required to view posts for a private club.");

                var hasStaffAccess = await _clubService.HasClubStaffAccessAsync(clubId, requestingUserId.Value, requestingUserRole);
                if (!hasStaffAccess)
                {
                    var membership = await _followRepository.IsFollowingClubAsync(clubId, requestingUserId.Value);
                    if (membership == null)
                        throw new ForbiddenException("You must be a member of this club to view its posts.");
                }
            }

            var post = await _cache.GetOrSetAsync(
                PostCacheKey(postId),
                () => _postRepository.GetByIdAsync(postId),
                PostTTL,
                nullSentinelTtl: NotFoundTTL);

            if (post == null || post.ClubId != clubId)
                throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            var users = await _userRepository.GetByIdsAsync([post.UserId]);
            return (post, users.Count > 0 ? users[0] : null);
        }

        public async Task<(List<ClubPost> Items, int TotalCount, string Source, Dictionary<int, UserListRecord> Authors)> GetByClubIdAsync(
            int clubId, int? requestingUserId, string? requestingUserRole, string? search, PostSortBy sortBy, int page, int pageSize)
        {
            var club = await _clubService.GetClub(clubId);

            if (club.isPrivate)
            {
                if (requestingUserId == null)
                    throw new UnauthorizedException("Authentication is required to view posts for a private club.");

                var hasStaffAccess = await _clubService.HasClubStaffAccessAsync(clubId, requestingUserId.Value, requestingUserRole);
                if (!hasStaffAccess)
                {
                    var membership = await _followRepository.IsFollowingClubAsync(clubId, requestingUserId.Value);
                    if (membership == null)
                        throw new ForbiddenException("You must be a member of this club to view its posts.");
                }
            }

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
                    return (posts, total, ResponseSource.Elasticsearch, await FetchAuthorLookupAsync(posts));
                }
                catch (ElasticsearchDisabledException ex)
                {
                    Logger.Info($"Elasticsearch disabled for club post search. Falling back to MySQL LIKE search. {ex.Message}");
                }
                catch (ElasticsearchUnavailableException ex)
                {
                    Logger.Warn(ex, "Elasticsearch temporarily unavailable. Falling back to MySQL LIKE search.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Club post Elasticsearch search failed with a non-fallback error: {ex}");
                    throw;
                }
            }

            var itemsTask = _postRepository.GetByClubIdAsync(clubId, search, sortBy, page, pageSize);
            var countTask = _postRepository.CountByClubIdAsync(clubId, search);
            await Task.WhenAll(itemsTask, countTask);

            var resultPosts = itemsTask.Result;
            if (resultPosts.Count > 0)
                await _postRepository.IncrementViewCountAsync(resultPosts.Select(p => p.Id));

            return (resultPosts, countTask.Result, ResponseSource.Database, await FetchAuthorLookupAsync(resultPosts));
        }

        public async Task<ClubPost> UpdateAsync(int clubId, int postId, int userId, string userRole, string title, string content, PostType postType, bool isPinned)
        {
            var post = await _postRepository.GetByIdAsync(postId)
                ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (post.ClubId != clubId)
                throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (!await _clubService.CanManageClubPostsAsync(clubId, userId, userRole))
                throw new ForbiddenException("You are not allowed to update this post.");

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var updated = await _postRepository.UpdateAsync(postId, new ClubPost
            {
                Title = title,
                Content = content,
                PostType = postType,
                IsPinned = isPinned
            }) ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            _outboxWriter.StageUpsert(updated);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await _cache.RemoveAsync(PostCacheKey(postId));

            return updated;
        }

        public async Task DeleteAsync(int clubId, int postId, int userId, string userRole)
        {
            var post = await _postRepository.GetByIdAsync(postId)
                ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (post.ClubId != clubId)
                throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (!await _clubService.CanManageClubPostsAsync(clubId, userId, userRole))
                throw new ForbiddenException("You are not allowed to delete this post.");

            await using var transaction = await _db.Database.BeginTransactionAsync();

            await _postRepository.DeleteAsync(postId);
            _outboxWriter.StageDelete(postId);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await _cache.RemoveAsync(PostCacheKey(postId));
        }

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
                    return (posts, total, ResponseSource.Elasticsearch);
                }
                catch (ElasticsearchDisabledException ex)
                {
                    Logger.Info($"Elasticsearch disabled for admin club post search. Falling back to MySQL LIKE search. {ex.Message}");
                }
                catch (ElasticsearchUnavailableException ex)
                {
                    Logger.Warn(ex, "Elasticsearch temporarily unavailable for admin club post search. Falling back to MySQL LIKE search.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Admin club post Elasticsearch search failed with a non-fallback error: {ex}");
                    throw;
                }
            }

            var itemsTask = _postRepository.GetAllAsync(search, sortBy, page, pageSize);
            var countTask = _postRepository.CountAllAsync(search);
            await Task.WhenAll(itemsTask, countTask);

            return (itemsTask.Result, countTask.Result, ResponseSource.Database);
        }

        private async Task<Dictionary<int, UserListRecord>> FetchAuthorLookupAsync(List<ClubPost> posts)
        {
            var userIds = posts.Select(p => p.UserId).Distinct().ToList();
            if (userIds.Count == 0) return [];
            var users = await _userRepository.GetByIdsAsync(userIds);
            return users.ToDictionary(u => u.Id);
        }
    }
}
