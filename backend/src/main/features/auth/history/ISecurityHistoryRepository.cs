namespace backend.main.features.auth.history;

/// <summary>
/// Read side of the account security history. The security-history worker owns every write.
/// </summary>
public interface ISecurityHistoryRepository
{
    /// <summary>
    /// One page of the account's unexpired entries, newest first, with the total unexpired count.
    /// <para>
    /// Expiry is filtered here rather than left to the purge: the purge runs periodically, so
    /// between an entry crossing its retention boundary and the next pass this filter is the only
    /// thing keeping the 90-day promise.
    /// </para>
    /// </summary>
    Task<(IReadOnlyList<AccountSecurityEvent> Items, int TotalCount)> GetPageAsync(
        int userId,
        DateTime now,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
