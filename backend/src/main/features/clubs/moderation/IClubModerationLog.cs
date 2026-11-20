namespace backend.main.features.clubs.moderation;

public interface IClubModerationLog
{
    /// <summary>
    /// Adds an entry to the request's database context without saving it. Stage it in the same
    /// save or transaction as the content change, and only once that change is certain to
    /// happen, so the log never records an action that did not take place.
    /// Staging an entry that is already staged does nothing, so a retried unit of work can stage
    /// the same instance again without writing it twice.
    /// </summary>
    void Stage(ClubModerationAction entry);

    /// <summary>A club's moderation log, newest first, for its managers, owner and admins.</summary>
    Task<(IReadOnlyList<ClubModerationAction> Items, int TotalCount)> GetPageAsync(
        int clubId, int userId, string? userRole, int page, int pageSize, CancellationToken cancellationToken = default);
}
