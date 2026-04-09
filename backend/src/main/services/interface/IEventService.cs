using backend.main.dtos.requests.events;
using backend.main.dtos.responses.events;
using backend.main.models.core;
using backend.main.models.enums;

namespace backend.main.services.interfaces
{
    public interface IEventsService
    {
        Task<Events> CreateEvent(
            int clubId,
            int userId,
            string name,
            string description,
            string location,
            IEnumerable<string> imageUrls,
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
            EventStatus? status = null,
            int page = 1,
            int pageSize = 20
        );

        Task<List<Events>> GetEventsByClub(
            int clubId,
            EventStatus? status = null,
            int page = 1,
            int pageSize = 20
        );

        Task<Events> UpdateEvent(
            int eventId,
            int userId,
            string name,
            string description,
            string location,
            IEnumerable<string>? imageUrls,
            DateTime startTime,
            DateTime? endTime,
            bool isPrivate,
            int maxParticipants,
            int registerCost
        );

        Task DeleteEvent(int eventId, int userId);

        // Batch operations
        Task<List<Events>> GetEventsByIds(IEnumerable<int> ids);
        Task<BatchCreateResultResponse> BatchCreateEvents(int clubId, int userId, IEnumerable<BatchCreateEventItem> items);
        Task<int> BatchUpdateEvents(int userId, IEnumerable<BatchUpdateEventItem> items);
        Task<int> BatchDeleteEvents(int userId, IEnumerable<int> ids);

        // Analytics
        Task<EventAnalyticsResponse> GetEventAnalytics(int eventId, int userId);
        Task<ClubAnalyticsResponse> GetClubAnalytics(int clubId, int userId);

        // Image management
        Task<PresignedUploadResponse> GenerateImageUploadUrlAsync(string fileName, string contentType);
        Task<EventImage> AddEventImageAsync(int eventId, int userId, string imageUrl);
        Task RemoveEventImageAsync(int eventId, int imageId, int userId);
    }
}
