using backend.main.Models;

namespace backend.main.Interfaces
{
    public interface IEventsService
    {
        Task<Events> CreateEvent(
            int clubId,
            int userId,
            string name,
            string description,
            string location,
            IFormFile image,
            DateTime startTime,
            DateTime? endTime,
            bool isPrivate = false,
            int maxParticipants = 100,
            int registerCost = 0
        );

        Task<Events> GetEvent(int eventId);

        Task<List<Events>> GetEvents(
            string? search = null,
            bool isPrivate = false,
            bool isAvailable = true,
            int page = 1,
            int pageSize = 20
        );

        Task<List<Events>> GetEventsByClub(
            int clubId,
            bool isPrivate = false,
            bool isAvailable = true,
            int page = 1,
            int pageSize = 20
        );

        Task<Events> UpdateEvent(
            int eventId,
            int userId,
            string name,
            string description,
            string location,
            IFormFile image,
            DateTime startTime,
            DateTime? endTime,
            bool isPrivate,
            int maxParticipants,
            int registerCost
        );

        Task DeleteEvent(int eventId, int userId);
    }
}
