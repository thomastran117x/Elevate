namespace backend.main.features.clubs.reviews
{
    public class ClubReview
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int ClubId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}


