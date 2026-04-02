namespace backend.main.dtos.responses.follow
{
    public class FollowResponse
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int ClubId { get; set; }
        public DateTime CreatedAt { get; set; }

        public FollowResponse(int id, int userId, int clubId, DateTime createdAt)
        {
            Id = id;
            UserId = userId;
            ClubId = clubId;
            CreatedAt = createdAt;
        }
    }
}
