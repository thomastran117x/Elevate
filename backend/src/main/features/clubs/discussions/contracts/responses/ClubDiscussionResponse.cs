using backend.main.shared.responses;

namespace backend.main.features.clubs.discussions.contracts.responses
{
    public class ClubDiscussionResponse
    {
        public int Id
        {
            get; set;
        }
        public int ClubId
        {
            get; set;
        }
        public int UserId
        {
            get; set;
        }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public AuthorInfo? Author
        {
            get; set;
        }
        public DateTime CreatedAt
        {
            get; set;
        }
        public DateTime UpdatedAt
        {
            get; set;
        }
        public int ReplyCount
        {
            get; set;
        }

        public ClubDiscussionResponse(int id, int clubId, int userId, string title, string description,
            DateTime createdAt, DateTime updatedAt)
        {
            Id = id;
            ClubId = clubId;
            UserId = userId;
            Title = title;
            Description = description;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }
    }
}
