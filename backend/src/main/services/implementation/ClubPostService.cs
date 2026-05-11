using backend.main.dtos.messages;
using backend.main.dtos.responses.general;
using backend.main.shared.exceptions.http;
using backend.main.models.core;
using backend.main.models.enums;
using backend.main.publishers.interfaces;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;
using backend.main.infrastructure.elasticsearch;

namespace backend.main.services.implementation
{
    public class ClubPostService : IClubPostService
    {
        private readonly IClubPostRepository _postRepository;
        private readonly IClubRepository _clubRepository;
        private readonly IFollowRepository _followRepository;
        private readonly IClubPostSearchService _searchService;
        private readonly IPublisher _publisher;

        public ClubPostService(
            IClubPostRepository postRepository,
            IClubRepository clubRepository,
            IFollowRepository followRepository,
            IClubPostSearchService searchService,
            IPublisher publisher)
        {
            _postRepository = postRepository;
            _clubRepository = clubRepository;
            _followRepository = followRepository;
            _searchService = searchService;
            _publisher = publisher;
        }

        public async Task<ClubPost> CreateAsync(int clubId, int userId, string title, string content, PostType postType, bool isPinned)
        {
            var club = await _clubRepository.GetByIdAsync(clubId)
                ?? throw new ResourceNotFoundException($"Club with ID {clubId} was not found.");

            if (club.UserId != userId)
                throw new ForbiddenException("Only the club owner can create posts.");

            var post = new ClubPost
            {
                ClubId = clubId,
                UserId = userId,
                Title = title,
                Content = content,
                PostType = postType,
                IsPinned = isPinned
            };

            post = await _postRepository.CreateAsync(post);
            await PublishIndexEventAsync(BuildUpsertEvent(post));
            return post;
        }

        public async Task<(List<ClubPost> Items, int TotalCount, string Source)> GetByClubIdAsync(
            int clubId, int? requestingUserId, string? search, PostSortBy sortBy, int page, int pageSize)
        {
            var club = await _clubRepository.GetByIdAsync(clubId)
                ?? throw new ResourceNotFoundException($"Club with ID {clubId} was not found.");

            if (club.isPrivate)
            {
                if (requestingUserId == null)
                    throw new UnauthorizedException("Authentication is required to view posts for a private club.");

                bool isOwner = club.UserId == requestingUserId.Value;
                if (!isOwner)
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
                    return (posts, total, ResponseSource.Elasticsearch);
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

            return (resultPosts, countTask.Result, ResponseSource.Database);
        }

        public async Task<ClubPost> UpdateAsync(int clubId, int postId, int userId, string title, string content, PostType postType, bool isPinned)
        {
            var post = await _postRepository.GetByIdAsync(postId)
                ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (post.ClubId != clubId)
                throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (post.UserId != userId)
                throw new ForbiddenException("You are not allowed to update this post.");

            var updated = await _postRepository.UpdateAsync(postId, new ClubPost
            {
                Title = title,
                Content = content,
                PostType = postType,
                IsPinned = isPinned
            }) ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            await PublishIndexEventAsync(BuildUpsertEvent(updated));
            return updated;
        }

        public async Task DeleteAsync(int clubId, int postId, int userId)
        {
            var post = await _postRepository.GetByIdAsync(postId)
                ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (post.ClubId != clubId)
                throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (post.UserId != userId)
                throw new ForbiddenException("You are not allowed to delete this post.");

            await _postRepository.DeleteAsync(postId);
            await PublishIndexEventAsync(new ClubPostIndexEvent { Operation = "delete", PostId = postId });
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

        private async Task PublishIndexEventAsync(ClubPostIndexEvent evt)
        {
            try
            {
                await _publisher.PublishAsync("clubpost-es-index", evt);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to publish ES index event for post {evt.PostId}. Index may be stale.");
            }
        }

        private static ClubPostIndexEvent BuildUpsertEvent(ClubPost post) => new()
        {
            Operation = "upsert",
            PostId = post.Id,
            ClubId = post.ClubId,
            UserId = post.UserId,
            Title = post.Title,
            Content = post.Content,
            PostType = post.PostType.ToString(),
            LikesCount = post.LikesCount,
            IsPinned = post.IsPinned,
            CreatedAt = post.CreatedAt,
            UpdatedAt = post.UpdatedAt
        };
    }
}
