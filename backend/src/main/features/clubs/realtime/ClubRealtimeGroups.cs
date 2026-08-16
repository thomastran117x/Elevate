namespace backend.main.features.clubs.realtime;

/// <summary>
/// Canonical SignalR group names and typing thread keys.
/// </summary>
/// <remarks>
/// The hub and the notifier both build names here so that a publish can never
/// miss its subscribers because the two sides spelled a group differently.
/// </remarks>
public static class ClubRealtimeGroups
{
    public const string DiscussionKind = "discussion";
    public const string PostKind = "post";

    /// <summary>
    /// Club-wide group: carries presence plus every discussion reply event for the club.
    /// Reply events were already club-wide under SSE, so clients keep filtering by discussion id.
    /// </summary>
    public static string Club(int clubId) => $"club:{clubId}";

    /// <summary>Post-scoped group: carries comment events for a single post.</summary>
    public static string Post(int clubId, int postId) => $"post:{clubId}:{postId}";

    /// <summary>
    /// Thread-scoped group used only for typing, so keystroke traffic never fans out club-wide.
    /// </summary>
    public static string Thread(string kind, int threadId) => $"thread:{kind}:{threadId}";

    public static string DiscussionThread(int discussionId) => Thread(DiscussionKind, discussionId);

    public static string PostThread(int postId) => Thread(PostKind, postId);
}
