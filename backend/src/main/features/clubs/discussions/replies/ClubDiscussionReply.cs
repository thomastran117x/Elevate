namespace backend.main.features.clubs.discussions.replies;

public class ClubDiscussionReply
{
    public int Id { get; set; }
    public int DiscussionId { get; set; }
    public int? ParentReplyId { get; set; }
    public int UserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
