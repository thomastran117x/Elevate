using backend.main.features.profile.contracts;

namespace backend.main.features.clubs.discussions
{
    public interface IClubDiscussionService
    {
        /// <summary>
        /// Starts a discussion in a club. The caller must be a member of the club (or owner/staff/admin),
        /// regardless of whether the club is public or private.
        /// </summary>
        Task<ClubDiscussion> CreateAsync(int clubId, int userId, string? userRole, string title, string description);

        /// <summary>
        /// Lists a club's discussions newest-first, together with the author records needed to render them.
        /// Readable anonymously for public clubs; private clubs require membership or staff access.
        /// </summary>
        Task<(IReadOnlyList<ClubDiscussion> Discussions, IReadOnlyDictionary<int, UserListRecord> Authors, int TotalCount)>
            GetByClubIdAsync(int clubId, int? requestingUserId, string? requestingUserRole, int page, int pageSize);

        /// <summary>Updates a discussion. Only the original author may do so.</summary>
        Task<ClubDiscussion> UpdateAsync(int clubId, int discussionId, int userId, string title, string description);

        /// <summary>Deletes a discussion. Only the original author may do so.</summary>
        Task DeleteAsync(int clubId, int discussionId, int userId);
    }
}
