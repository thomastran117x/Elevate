namespace backend.main.features.clubs.realtime.contracts.responses;

/// <summary>A single online member, as broadcast to club subscribers.</summary>
public sealed record PresenceUser(int UserId, string? Name, string? Username, string? Avatar);

/// <summary>
/// The full roster, sent once to a caller when it joins a club.
/// <paramref name="Users"/> is capped; <paramref name="TotalOnline"/> is not, so large
/// clubs can render "+N more".
/// </summary>
public sealed record PresenceSnapshot(
    int ClubId,
    IReadOnlyList<PresenceUser> Users,
    int TotalOnline);

/// <summary>
/// An incremental roster change broadcast to everyone already in the club. Exactly one of
/// <paramref name="Joined"/> or <paramref name="LeftUserId"/> is set.
/// </summary>
public sealed record PresenceDiff(
    int ClubId,
    PresenceUser? Joined,
    int? LeftUserId,
    int TotalOnline);

/// <summary>Everyone currently typing in one thread. An empty list clears the indicator.</summary>
public sealed record ThreadTypingSnapshot(string ThreadKey, IReadOnlyList<PresenceUser> Users);
