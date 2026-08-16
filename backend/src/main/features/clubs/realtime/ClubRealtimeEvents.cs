namespace backend.main.features.clubs.realtime;

/// <summary>
/// Client-bound event names. The reply and comment names are carried over verbatim from
/// the SSE contract they replace.
/// </summary>
public static class ClubRealtimeEvents
{
    public const string ReplyCreated = "ReplyCreated";
    public const string ReplyUpdated = "ReplyUpdated";
    public const string ReplyDeleted = "ReplyDeleted";
    public const string ReplyReactionChanged = "ReplyReactionChanged";

    public const string CommentCreated = "CommentCreated";
    public const string CommentUpdated = "CommentUpdated";
    public const string CommentDeleted = "CommentDeleted";
    public const string CommentReactionChanged = "CommentReactionChanged";

    /// <summary>Full roster, sent only to the caller that just joined a club.</summary>
    public const string PresenceSnapshot = "PresenceSnapshot";

    /// <summary>Incremental roster change, broadcast to the rest of the club.</summary>
    public const string PresenceChanged = "PresenceChanged";

    /// <summary>Current typing roster for one thread.</summary>
    public const string TypingChanged = "TypingChanged";
}
