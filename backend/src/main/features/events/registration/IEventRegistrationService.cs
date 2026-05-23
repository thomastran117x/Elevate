using backend.main.features.events.registration;
using backend.main.features.events.registration.contracts.responses;

namespace backend.main.features.events.registration
{
    public interface IEventRegistrationService
    {
        Task RegisterAsync(int eventId, int userId, string userRole);
        Task UnregisterAsync(int eventId, int userId, string userRole);
        Task<bool> IsRegisteredAsync(int eventId, int userId, string userRole);
        Task<IEnumerable<EventRegistration>> GetRegistrationsByEventAsync(int eventId, int page = 1, int pageSize = 20);
        Task<IEnumerable<EventRegistration>> GetRegistrationsByUserAsync(int userId, int page = 1, int pageSize = 20);
        Task<BatchRegistrationResultResponse> BatchRegisterAsync(int userId, string userRole, IEnumerable<int> eventIds);
        Task<BatchRegistrationResultResponse> BatchUnregisterAsync(int userId, string userRole, IEnumerable<int> eventIds);
    }
}


