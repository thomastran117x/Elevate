using backend.main.features.events.contracts.responses;

namespace backend.main.shared.storage
{
    public interface IAzureBlobService
    {
        Task<PresignedUploadResponse> GenerateUploadUrlAsync(string blobPathPrefix, string fileName, string contentType);
        bool IsOwnedBlobUrl(string blobUrl);
        Task DeleteBlobAsync(string blobUrl);
    }
}
