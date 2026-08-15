using backend.main.shared.responses;

namespace backend.main.features.clubs.posts.comments.contracts.responses
{
    public class PostCommentResponse
    {
        public int Id
        {
            get; set;
        }
        public int PostId
        {
            get; set;
        }
        public int? ParentCommentId
        {
            get; set;
        }
        public int? UserId
        {
            get; set;
        }
        public string? Content
        {
            get; set;
        }
        public AuthorInfo? Author
        {
            get; set;
        }
        public DateTime CreatedAt
        {
            get; set;
        }
        public DateTime UpdatedAt
        {
            get; set;
        }
        public bool IsDeleted
        {
            get; set;
        }
        public int LikeCount
        {
            get; set;
        }
        public int DislikeCount
        {
            get; set;
        }
        public string? CurrentUserReaction
        {
            get; set;
        }
        public int DirectReplyCount
        {
            get; set;
        }

        public PostCommentResponse(int id, int postId, int? parentCommentId, int? userId, string? content,
            DateTime createdAt, DateTime updatedAt)
        {
            Id = id;
            PostId = postId;
            ParentCommentId = parentCommentId;
            UserId = userId;
            Content = content;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }
    }

    public sealed class PostCommentPageResponse
    {
        public IEnumerable<PostCommentResponse> Items { get; init; } = [];
        public int TotalCount
        {
            get; init;
        }
        public string? NextCursor
        {
            get; init;
        }
        public bool HasMore
        {
            get; init;
        }
    }

    public sealed class PostCommentReactionResponse
    {
        public int CommentId
        {
            get; init;
        }
        public int LikeCount
        {
            get; init;
        }
        public int DislikeCount
        {
            get; init;
        }
        public string? CurrentUserReaction
        {
            get; init;
        }
    }
}
