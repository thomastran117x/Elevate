using backend.main.features.events.registration;

namespace backend.main.features.events.registration
{
    public interface IEventRegistrationRepository
    {
        /// <summary>
        /// Atomically re-reads the event, validates it is still open and has capacity, then inserts the
        /// registration — all within a SERIALIZABLE transaction. Returns the created registration, or null
        /// if the event does not exist, is closed (EndTime in the past), or is at capacity.
        /// Throws DbUpdateException on duplicate (EventId, UserId).
        /// </summary>
        Task<EventRegistration?> TryRegisterAsync(int eventId, int userId);
        Task<bool> UnregisterAsync(int eventId, int userId);
        Task<EventRegistration?> IsRegisteredAsync(int eventId, int userId);
        Task<IEnumerable<EventRegistration>> GetRegistrationsByEventAsync(int eventId, int page = 1, int pageSize = 20);
        Task<IEnumerable<EventRegistration>> GetRegistrationsByUserAsync(int userId, int page = 1, int pageSize = 20);
    }
}


