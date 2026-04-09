using backend.main.dtos.responses.eventregistration;
using backend.main.models.core;

namespace backend.main.services.interfaces
{
    public interface IEventRegistrationService
    {
        Task RegisterAsync(int eventId, int userId);
        Task UnregisterAsync(int eventId, int userId);
        Task<bool> IsRegisteredAsync(int eventId, int userId);
        Task<IEnumerable<EventRegistration>> GetRegistrationsByEventAsync(int eventId, int page = 1, int pageSize = 20);
        Task<IEnumerable<EventRegistration>> GetRegistrationsByUserAsync(int userId, int page = 1, int pageSize = 20);
        Task<BatchRegistrationResultResponse> BatchRegisterAsync(int userId, IEnumerable<int> eventIds);
        Task<BatchRegistrationResultResponse> BatchUnregisterAsync(int userId, IEnumerable<int> eventIds);
    }
}
