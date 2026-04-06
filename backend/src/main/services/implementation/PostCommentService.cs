using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;

namespace backend.main.services.implementation
{
    public class PostCommentService : IPostCommentService
    {
        private readonly IPostCommentRepository _commentRepository;
        private readonly IClubPostRepository _postRepository;
        private readonly IClubRepository _clubRepository;

        public PostCommentService(
            IPostCommentRepository commentRepository,
            IClubPostRepository postRepository,
            IClubRepository clubRepository)
        {
            _commentRepository = commentRepository;
            _postRepository = postRepository;
            _clubRepository = clubRepository;
        }

        public async Task<PostComment> CreateAsync(int clubId, int postId, int userId, string content)
        {
            await ValidatePostBelongsToClub(clubId, postId);

            var comment = new PostComment
            {
                PostId = postId,
                UserId = userId,
                Content = content
            };

            return await _commentRepository.CreateAsync(comment);
        }

        public async Task<(List<PostComment> Items, int TotalCount)> GetByPostIdAsync(
            int clubId, int postId, int page, int pageSize)
        {
            await ValidatePostBelongsToClub(clubId, postId);

            var itemsTask = _commentRepository.GetByPostIdAsync(postId, page, pageSize);
            var countTask = _commentRepository.CountByPostIdAsync(postId);
            await Task.WhenAll(itemsTask, countTask);

            return (itemsTask.Result, countTask.Result);
        }

        public async Task<PostComment> UpdateAsync(int postId, int commentId, int userId, string content)
        {
            var comment = await _commentRepository.GetByIdAsync(commentId)
                ?? throw new ResourceNotFoundException($"Comment with ID {commentId} was not found.");

            if (comment.PostId != postId)
                throw new ResourceNotFoundException($"Comment with ID {commentId} was not found.");

            if (comment.UserId != userId)
                throw new ForbiddenException("You are not allowed to update this comment.");

            return await _commentRepository.UpdateAsync(commentId, new PostComment { Content = content })
                ?? throw new ResourceNotFoundException($"Comment with ID {commentId} was not found.");
        }

        public async Task DeleteAsync(int postId, int commentId, int userId)
        {
            var comment = await _commentRepository.GetByIdAsync(commentId)
                ?? throw new ResourceNotFoundException($"Comment with ID {commentId} was not found.");

            if (comment.PostId != postId)
                throw new ResourceNotFoundException($"Comment with ID {commentId} was not found.");

            if (comment.UserId != userId)
                throw new ForbiddenException("You are not allowed to delete this comment.");

            await _commentRepository.DeleteAsync(commentId);
        }

        private async Task ValidatePostBelongsToClub(int clubId, int postId)
        {
            _ = await _clubRepository.GetByIdAsync(clubId)
                ?? throw new ResourceNotFoundException($"Club with ID {clubId} was not found.");

            var post = await _postRepository.GetByIdAsync(postId)
                ?? throw new ResourceNotFoundException($"Post with ID {postId} was not found.");

            if (post.ClubId != clubId)
                throw new ResourceNotFoundException($"Post with ID {postId} was not found.");
        }
    }
}
