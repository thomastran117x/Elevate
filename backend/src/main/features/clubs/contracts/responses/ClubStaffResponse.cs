namespace backend.main.features.clubs.contracts.responses
{
    public class ClubStaffResponse
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int UserId { get; set; }
        public string Role { get; set; } = string.Empty;
        public int GrantedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public ClubStaffResponse(
            int id,
            int clubId,
            int userId,
            string role,
            int grantedByUserId,
            DateTime createdAt,
            DateTime updatedAt)
        {
            Id = id;
            ClubId = clubId;
            UserId = userId;
            Role = role;
            GrantedByUserId = grantedByUserId;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }
    }
}
