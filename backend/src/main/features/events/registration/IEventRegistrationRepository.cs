using backend.main.features.events.registration;

namespace backend.main.features.events.registration
{
    public interface IEventRegistrationRepository
    {
        Task<EventRegistration?> IsRegisteredAsync(int eventId, int userId);
        Task<IEnumerable<EventRegistration>> GetRegistrationsByEventAsync(int eventId, int page = 1, int pageSize = 20);
        Task<IEnumerable<EventRegistration>> GetRegistrationsByUserAsync(int userId, int page = 1, int pageSize = 20);
    }
}
