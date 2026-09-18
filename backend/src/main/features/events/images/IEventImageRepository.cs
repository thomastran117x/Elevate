using backend.main.features.events.images;

namespace backend.main.features.events.images
{
    public interface IEventImageRepository
    {
        Task<List<EventImage>> GetByEventIdAsync(int eventId);
        Task<EventImage?> GetByIdAsync(int id, int eventId);
        Task<List<EventImage>> AddImagesAsync(int eventId, IEnumerable<string> imageUrls);
        Task<EventImage> AddImageAsync(
            int eventId,
            string imageUrl,
            string? altText,
            bool isDecorative);
        Task<bool> UpdateMetadataAsync(int imageId, int eventId, string? altText, bool isDecorative);
        Task<bool> ReplaceImageUrlAsync(int imageId, int eventId, string imageUrl);

        /// <summary>
        /// Reconciles the event's images against <paramref name="orderedUrls"/>, keeping the rows
        /// whose URL survives so their alt text, cover flag and id are preserved.
        /// </summary>
        Task SyncImagesAsync(int eventId, IReadOnlyList<string> orderedUrls);

        Task ReorderAsync(int eventId, IReadOnlyList<int> imageIds);
        Task SetCoverAsync(int eventId, int imageId);

        /// <summary>Promotes the first image to cover when the event has images but no cover.</summary>
        Task EnsureCoverAsync(int eventId);

        Task<bool> DeleteImageAsync(int imageId, int eventId);
        Task DeleteAllByEventIdAsync(int eventId);
        Task<int> CountByEventIdAsync(int eventId);
    }
}
