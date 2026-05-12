namespace backend.main.features.clubs.posts.comments.contracts.responses
{
    public class PostCommentResponse
    {
        public int Id { get; set; }
        public int PostId { get; set; }
        public int UserId { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public PostCommentResponse(int id, int postId, int userId, string content, DateTime createdAt, DateTime updatedAt)
        {
            Id = id;
            PostId = postId;
            UserId = userId;
            Content = content;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }
    }
}
