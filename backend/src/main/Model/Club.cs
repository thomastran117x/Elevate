using backend.main.Common;

namespace backend.main.Models
{
    public class Club
    {
        public int Id
        {
            get; set;
        }
        public required string Name
        {
            get; set;
        }
        public required string Description
        {
            get; set;
        }
        public required ClubType Clubtype
        {
            get; set;
        }
        public required string ClubImage
        {
            get; set;
        }
        public string? Phone
        {
            get; set;
        }
        public string? Email
        {
            get; set;
        }
        public double? Rating
        {
            get; set;
        }
        public string? WebsiteUrl
        {
            get; set;
        }
        public string? Location
        {
            get; set;
        }
        public int MemberCount { get; set; } = 0;
        public int EventCount { get; set; } = 0;
        public int AvaliableEventCount { get; set; } = 0;
        public int MaxMemberCount { get; set; } = 1000;
        public bool isPrivate { get; set; } = false;
        public int UserId
        {
            get; set;
        }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
