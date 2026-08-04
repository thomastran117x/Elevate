namespace backend.main.features.events.waitlist
{
    /// <summary>
    /// Read-only access to waitlist entries. Mutating paths go through the service's
    /// DbContext directly so they participate in its Serializable transaction.
    /// </summary>
    public interface IEventWaitlistRepository
    {
        Task<EventWaitlistEntry?> GetEntryAsync(int eventId, int userId);

        Task<EventWaitlistEntry?> GetEntryByIdAsync(int eventId, int entryId);

        Task<IReadOnlyList<EventWaitlistEntry>> GetWaitingByEventAsync(int eventId, int page = 1, int pageSize = 20);

        Task<int> CountWaitingAsync(int eventId);

        /// <summary>
        /// 1-based place in the queue, computed from (JoinedAtUtc, Id) rather than stored.
        /// </summary>
        Task<int> GetPositionAsync(int eventId, DateTime joinedAtUtc, int entryId);

        Task<IReadOnlyList<EventWaitlistEntry>> GetWaitingByUserAsync(int userId);
    }
}
