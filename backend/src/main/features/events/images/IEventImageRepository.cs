using backend.main.models.core;

namespace backend.main.features.events.images
{
    public interface IEventImageRepository
    {
        Task<List<EventImage>> GetByEventIdAsync(int eventId);
        Task<EventImage?> GetByIdAsync(int id, int eventId);
        Task<List<EventImage>> AddImagesAsync(int eventId, IEnumerable<string> imageUrls);
        Task<bool> DeleteImageAsync(int imageId, int eventId);
        Task DeleteAllByEventIdAsync(int eventId);
        Task<int> CountByEventIdAsync(int eventId);
    }
}
