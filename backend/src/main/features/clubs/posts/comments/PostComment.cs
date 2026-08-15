namespace backend.main.features.clubs.posts.comments
{
    public class PostComment
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
        public int UserId
        {
            get; set;
        }
        public string Content { get; set; } = string.Empty;
        public bool IsDeleted
        {
            get; set;
        }
        public DateTime? DeletedAt
        {
            get; set;
        }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

