using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.models.enums;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;

namespace backend.main.services.implementation
{
    public class ClubPostService : IClubPostService
    {
        private readonly IClubPostRepository _postRepository;
        private readonly IClubRepository _clubRepository;
        private readonly IFollowRepository _followRepository;

        public ClubPostService(
            IClubPostRepository postRepository,
            IClubRepository clubRepository,
            IFollowRepository followRepository)
        {
            _postRepository = postRepository;
            _clubRepository = clubRepository;
            _followRepository = followRepository;
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

            return await _postRepository.CreateAsync(post);
        }

        public async Task<(List<ClubPost> Items, int TotalCount)> GetByClubIdAsync(
            int clubId, int? requestingUserId, string? search, int page, int pageSize)
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

            var itemsTask = _postRepository.GetByClubIdAsync(clubId, search, page, pageSize);
            var countTask = _postRepository.CountByClubIdAsync(clubId, search);
            await Task.WhenAll(itemsTask, countTask);

            var posts = itemsTask.Result;
            if (posts.Count > 0)
                await _postRepository.IncrementViewCountAsync(posts.Select(p => p.Id));

            return (posts, countTask.Result);
        }

        public async Task<ClubPost> UpdateAsync(int clubId, int postId, int userId, string title, string content, PostType postType, bool isPinned)
        {
            var post = await _postRepository.GetByIdAsync(postId)
                ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (post.ClubId != clubId)
                throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (post.UserId != userId)
                throw new ForbiddenException("You are not allowed to update this post.");

            return await _postRepository.UpdateAsync(postId, new ClubPost
            {
                Title = title,
                Content = content,
                PostType = postType,
                IsPinned = isPinned
            }) ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");
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
        }

        public async Task<(List<ClubPost> Items, int TotalCount)> GetAllAdminAsync(
            string? search, int page, int pageSize)
        {
            var itemsTask = _postRepository.GetAllAsync(search, page, pageSize);
            var countTask = _postRepository.CountAllAsync(search);
            await Task.WhenAll(itemsTask, countTask);

            return (itemsTask.Result, countTask.Result);
        }
    }
}
