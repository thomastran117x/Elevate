namespace backend.main.features.clubs.moderation;

public interface IClubModerationLog
{
    /// <summary>
    /// Adds an entry to the request's database context without saving it. Call it before the
    /// content write, so the entry commits in the same save or transaction as the change.
    /// </summary>
    void Stage(ClubModerationAction entry);

    /// <summary>A club's moderation log, newest first, for its managers, owner and admins.</summary>
    Task<(IReadOnlyList<ClubModerationAction> Items, int TotalCount)> GetPageAsync(
        int clubId, int userId, string? userRole, int page, int pageSize, CancellationToken cancellationToken = default);
}
