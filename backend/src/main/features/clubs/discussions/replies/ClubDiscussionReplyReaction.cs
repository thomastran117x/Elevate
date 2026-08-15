namespace backend.main.features.clubs.discussions.replies;

public enum DiscussionReplyReactionType
{
    Like = 0,
    Dislike = 1
}

public class ClubDiscussionReplyReaction
{
    public int ReplyId { get; set; }
    public int UserId { get; set; }
    public DiscussionReplyReactionType Reaction { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
