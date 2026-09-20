using backend.main.features.events.contracts.responses;

namespace backend.main.shared.storage
{
    public interface IAzureBlobService
    {
        /// <summary>
        /// Largest blob this service accepts when one is attached, from
        /// <c>ImageUpload:MaxBytes</c>.
        /// </summary>
        long MaxImageBytes
        {
            get;
        }

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

        /// <summary>
        /// Reads a stored blob's length, stored content type and leading bytes without
        /// downloading it: properties plus a ranged read of <paramref name="prefixByteCount"/>
        /// bytes. Returns null when the blob does not exist, the URL is not one of ours, or
        /// storage is not configured.
        /// </summary>
        /// <remarks>
        /// The presigned upload path never sees the bytes — the browser PUTs them straight to
        /// Azure — so this is how the server inspects what actually landed.
        /// </remarks>
        Task<BlobInspection?> InspectBlobAsync(
            string blobUrl,
            int prefixByteCount = ImageSignatureInspector.HeaderByteCount,
            CancellationToken cancellationToken = default);
    }

    public readonly record struct BlobListItem(string Url, DateTimeOffset? LastModified);

    /// <summary>
    /// What a stored blob turned out to be, as opposed to what its uploader declared.
    /// </summary>
    public readonly record struct BlobInspection(
        long ContentLength,
        string? ContentType,
        byte[] HeaderBytes);
}
