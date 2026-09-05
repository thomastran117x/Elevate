namespace backend.main.features.auth;

/// <summary>
/// How much a caller is willing to trust the bloom filter.
/// </summary>
public enum UsernameLookupMode
{
    /// <summary>
    /// Always confirm against the database. Required on any path that is about to claim the
    /// name: the local filter can lag another instance by up to one refresh interval, and a
    /// wrongly optimistic answer there turns a clean conflict into a unique-index violation.
    /// </summary>
    Authoritative = 0,

    /// <summary>
    /// Let the filter answer when it proves the name is free. For read-only probes, where a
    /// briefly stale answer costs a late error message and nothing else.
    /// </summary>
    Advisory = 1,
}

/// <summary>
/// Answers "is this username already spoken for", optionally using the bloom filter to skip the
/// database when it can prove the name is free.
/// </summary>
public interface IUsernameAvailabilityService
{
    /// <summary>
    /// True when the username is held by a user or covered by an active reservation.
    /// </summary>
    /// <remarks>
    /// Same contract as <c>IAuthUserRepository.UsernameUnavailableAsync</c>, and deliberately the
    /// same polarity so call sites read identically. A false answer is authoritative for the
    /// instant it is produced; it is not a reservation, and the unique index remains the thing
    /// that actually prevents a duplicate.
    /// </remarks>
    /// <param name="normalizedUsername">Username already passed through <c>UsernamePolicy</c>.</param>
    /// <param name="utcNow">Clock reading used to test reservation expiry.</param>
    /// <param name="mode">Whether the filter may answer on its own. Defaults to authoritative.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<bool> IsUnavailableAsync(
        string normalizedUsername,
        DateTime utcNow,
        UsernameLookupMode mode = UsernameLookupMode.Authoritative,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a username is now taken. Call after the claiming write has committed.
    /// </summary>
    Task MarkTakenAsync(string normalizedUsername, CancellationToken cancellationToken = default);
}
