namespace backend.main.features.clubs.posts.search
{
    public sealed record ClubPostIndexEvent
    {
        public required string Operation { get; init; }
        public required int PostId { get; init; }
        public int? ClubId { get; init; }
        public int? UserId { get; init; }
        public string? Title { get; init; }
        public string? Content { get; init; }
        public string? PostType { get; init; }
        public int? LikesCount { get; init; }
        public bool? IsPinned { get; init; }
        public DateTime? CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }
    }
}
