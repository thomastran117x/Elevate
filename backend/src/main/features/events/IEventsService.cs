using backend.main.features.events.contracts.requests;
using backend.main.features.events.contracts.responses;
using backend.main.models.core;
using backend.main.models.enums;
using backend.main.features.events.search;

namespace backend.main.features.events
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
            bool isPrivate,
            int maxParticipants,
            int registerCost,
            EventCategory category,
            string? venueName,
            string? city,
            double? latitude,
            double? longitude,
            List<string>? tags
        );

        Task<Events> GetEvent(int eventId);
        Task<Events> GetVisibleEvent(int eventId, int? userId = null);

        Task<(List<Events> Events, int TotalCount, Dictionary<int, double> DistanceKmById, string Source)> GetEvents(EventSearchCriteria criteria);

        Task<(List<Events> Events, int TotalCount, string Source)> GetEventsByClub(
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
            int registerCost,
            EventCategory category,
            string? venueName,
            string? city,
            double? latitude,
            double? longitude,
            List<string>? tags
        );

        Task DeleteEvent(int eventId, int userId);

        // Batch operations
        Task<List<Events>> GetEventsByIds(IEnumerable<int> ids);
        Task<List<Events>> GetVisibleEventsByIds(IEnumerable<int> ids, int? userId = null);
        Task<BatchCreateResultResponse> BatchCreateEvents(int clubId, int userId, IEnumerable<BatchCreateEventItem> items);
        Task<int> BatchUpdateEvents(int userId, IEnumerable<BatchUpdateEventItem> items);
        Task<int> BatchDeleteEvents(int userId, IEnumerable<int> ids);

        // Analytics
        Task<EventAnalyticsResponse> GetEventAnalytics(int eventId, int userId);
        Task<ClubAnalyticsResponse> GetClubAnalytics(int clubId, int userId);

        // Image management
        Task<PresignedUploadResponse> GenerateImageUploadUrlAsync(
            int clubId,
            int userId,
            string fileName,
            string contentType,
            int? eventId = null);
        Task<EventImage> AddEventImageAsync(int eventId, int userId, string imageUrl);
        Task RemoveEventImageAsync(int eventId, int imageId, int userId);

        // Registration count denorm (called by EventRegistrationService)
        Task NotifyRegistrationChangedAsync(int eventId);
    }
}
