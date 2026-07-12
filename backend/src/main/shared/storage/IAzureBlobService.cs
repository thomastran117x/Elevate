using backend.main.features.events.contracts.responses;

namespace backend.main.shared.storage
{
    public interface IAzureBlobService
    {
        Task<string> UploadImageAsync(IFormFile image, string blobPathPrefix);
        Task<PresignedUploadResponse> GenerateUploadUrlAsync(string blobPathPrefix, string fileName, string contentType);
        bool IsOwnedBlobUrl(string blobUrl);
        Task DeleteBlobAsync(string blobUrl);

        /// <summary>
        /// Lists blobs under the given path prefix, returning each blob's full public URL
        /// (matching the stored-URL format) and last-modified time. Yields nothing when
        /// storage is not configured.
        /// </summary>
        IAsyncEnumerable<BlobListItem> ListBlobsAsync(string blobPathPrefix, CancellationToken cancellationToken = default);
    }

    public readonly record struct BlobListItem(string Url, DateTimeOffset? LastModified);
}
