using backend.main.dtos.responses.events;

namespace backend.main.services.interfaces
{
    public interface IAzureBlobService
    {
        Task<PresignedUploadResponse> GenerateUploadUrlAsync(string fileName, string contentType);
        Task DeleteBlobAsync(string blobUrl);
    }
}
