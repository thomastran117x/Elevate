namespace backend.main.features.clubs.follow.contracts.responses
{
    /// <summary>
    /// A club member (follow record) enriched with the member's public profile
    /// fields. Profile fields are null when the referenced user no longer exists.
    /// </summary>
    public class ClubMemberResponse
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

        public ClubMemberResponse(
            int id,
            int userId,
            int clubId,
            DateTime createdAt,
            string? name,
            string? username,
            string? avatar)
        {
            Id = id;
            UserId = userId;
            ClubId = clubId;
            CreatedAt = createdAt;
            Name = name;
            Username = username;
            Avatar = avatar;
        }
    }
}
