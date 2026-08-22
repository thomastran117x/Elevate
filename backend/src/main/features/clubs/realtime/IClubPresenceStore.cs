using backend.main.features.clubs.realtime.contracts.responses;

namespace backend.main.features.clubs.realtime;

/// <summary>
/// Presence ("who is online in this club") and typing ("who is typing in this thread")
/// state for realtime connections.
/// </summary>
/// <remarks>
/// The default implementation is process-local by design: the deployment runs a single
/// backend replica, exactly as the SSE brokers this replaces already assumed. Scaling
/// past one replica requires a Redis-backed implementation of this interface *together
/// with* a SignalR backplane — fanning out messages without also sharing presence would
/// leave every replica reporting its own partial roster.
/// </remarks>
public interface IClubPresenceStore
{
    /// <summary>
    /// Registers a connection against a club. Pass a null <paramref name="user"/> for
    /// anonymous viewers: they are tracked for cleanup but never appear in the roster.
    /// </summary>
    /// <returns>True when this made the user newly visible in the club.</returns>
    bool JoinClub(int clubId, string connectionId, PresenceUser? user);

    /// <summary>
    /// Drops one connection from a club. A user stays online while any of their other
    /// connections (tabs) remain.
    /// </summary>
    /// <returns>True when this removed the user's last connection to the club.</returns>
    bool LeaveClub(int clubId, string connectionId, out PresenceUser? user);

    /// <summary>Clubs this connection joined, for disconnect cleanup.</summary>
    IReadOnlyList<int> ClubsFor(string connectionId);

    /// <summary>The capped roster plus an uncapped online count.</summary>
    PresenceSnapshot Snapshot(int clubId);

    /// <summary>Registers that a connection is subscribed to a thread's typing group.</summary>
    void JoinThread(string connectionId, string threadKey);

    /// <summary>
    /// Unsubscribes a connection from a thread, clearing any typing entry it held.
    /// </summary>
    /// <returns>True when the thread's typing roster changed as a result.</returns>
    bool LeaveThread(string connectionId, string threadKey);

    /// <summary>
    /// Whether a connection joined a thread. Typing is authorized against this rather than
    /// re-querying the database on every throttled keystroke.
    /// </summary>
    bool IsInThread(string connectionId, string threadKey);

    /// <summary>Threads this connection joined, for disconnect cleanup.</summary>
    IReadOnlyList<string> ThreadsFor(string connectionId);

    /// <summary>Marks a connection as typing (or not) in a thread, refreshing its TTL.</summary>
    /// <returns>True when the thread's typing roster changed as a result.</returns>
    bool SetTyping(string threadKey, string connectionId, PresenceUser user, bool isTyping, DateTimeOffset now);

    /// <summary>
    /// Drops typing entries whose TTL lapsed — the case where a client never sent an
    /// explicit stop because its tab closed or its network dropped.
    /// </summary>
    /// <returns>Thread keys whose typing roster changed.</returns>
    IReadOnlyList<string> ExpireTyping(DateTimeOffset now);

    /// <summary>Everyone currently typing in a thread, deduplicated across a user's tabs.</summary>
    ThreadTypingSnapshot Typing(string threadKey);
}
