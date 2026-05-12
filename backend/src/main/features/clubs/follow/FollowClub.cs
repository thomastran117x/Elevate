namespace backend.main.features.clubs.follow
{
    public class FollowClub
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
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}


