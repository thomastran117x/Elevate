namespace backend.main.features.clubs.posts
{
    public class ClubPost
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public PostType PostType { get; set; } = PostType.General;
        public int LikesCount { get; set; } = 0;
        public int ViewCount { get; set; } = 0;
        public bool IsPinned { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}


