namespace backend.main.features.clubs.posts.comments;

public enum PostCommentReactionType
{
    Like = 0,
    Dislike = 1
}

public class PostCommentReaction
{
    public int CommentId
    {
        get; set;
    }
    public int UserId
    {
        get; set;
    }
    public PostCommentReactionType Reaction
    {
        get; set;
    }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
