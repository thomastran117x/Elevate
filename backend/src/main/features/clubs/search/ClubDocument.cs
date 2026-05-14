namespace backend.main.features.clubs.search
{
    public sealed class ClubDocument
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ClubType { get; set; } = string.Empty;
        public string? Location { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public int MemberCount { get; set; }
        public double? Rating { get; set; }
        public bool IsPrivate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
