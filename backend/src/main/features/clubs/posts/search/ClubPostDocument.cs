namespace backend.main.features.clubs.posts.search
{
    public class ClubPostDocument
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string PostType { get; set; } = string.Empty;
        public int LikesCount { get; set; }
        public bool IsPinned { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
