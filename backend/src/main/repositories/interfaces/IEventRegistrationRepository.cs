using backend.main.models.core;

namespace backend.main.repositories.interfaces
{
    public interface IEventRegistrationRepository
    {
        /// <summary>
        /// Atomically checks capacity and inserts the registration within a SERIALIZABLE transaction.
        /// Returns the created registration, or null if the event is full.
        /// Throws DbUpdateException on duplicate (EventId, UserId).
        /// </summary>
        Task<EventRegistration?> TryRegisterAsync(int eventId, int userId, int maxParticipants);
        Task<bool> UnregisterAsync(int eventId, int userId);
        Task<EventRegistration?> IsRegisteredAsync(int eventId, int userId);
        Task<IEnumerable<EventRegistration>> GetRegistrationsByEventAsync(int eventId, int page = 1, int pageSize = 20);
        Task<IEnumerable<EventRegistration>> GetRegistrationsByUserAsync(int userId, int page = 1, int pageSize = 20);
    }
}
