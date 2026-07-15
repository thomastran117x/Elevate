namespace backend.main.features.clubs
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
        /// <summary>Optional hero/banner image, separate from the square club icon (<see cref="ClubImage"/>).</summary>
        public string? BannerImage
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
        public int CurrentVersionNumber { get; set; } = 0;
        public int UserId
        {
            get; set;
        }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}


