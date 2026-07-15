namespace backend.main.features.clubs.reviews.contracts.responses
{
    public class ClubReviewResponse
    {
        public int Id
        {
            get; set;
        }
        public int UserId
        {
            get; set;
        }
        public int ClubId
        {
            get; set;
        }
        public string Title { get; set; } = string.Empty;
        public int Rating
        {
            get; set;
        }
        public string? Comment
        {
            get; set;
        }
        public DateTime CreatedAt
        {
            get; set;
        }
        public string? Name
        {
            get; set;
        }
        public string? Username
        {
            get; set;
        }
        public string? Avatar
        {
            get; set;
        }

        public ClubReviewResponse(int id, int userId, int clubId, string title, int rating, string? comment, DateTime createdAt,
            string? name = null, string? username = null, string? avatar = null)
        {
            Id = id;
            UserId = userId;
            ClubId = clubId;
            Title = title;
            Rating = rating;
            Comment = comment;
            CreatedAt = createdAt;
            Name = name;
            Username = username;
            Avatar = avatar;
        }
    }
}
