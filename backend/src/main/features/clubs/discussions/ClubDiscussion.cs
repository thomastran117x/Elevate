using System.ComponentModel.DataAnnotations.Schema;

namespace backend.main.features.clubs.discussions
{
    public class ClubDiscussion
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
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [NotMapped]
        public int ReplyCount
        {
            get; set;
        }
    }
}
