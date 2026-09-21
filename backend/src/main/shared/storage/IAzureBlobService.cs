using backend.main.features.events.contracts.responses;
using backend.main.shared.storage.imaging;

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

        /// <summary>
        /// Stores an image the <see cref="imaging.IImageProcessor"/> has already re-encoded and
        /// returns its public URL. The blob is named and typed from the processed output, never
        /// from anything the uploader supplied.
        /// </summary>
        /// <remarks>
        /// There is deliberately no way to store raw upload bytes through this service: the
        /// container is anonymously readable, and unprocessed bytes carry EXIF and anything else
        /// the uploader hid after the image header.
        /// </remarks>
        Task<string> UploadProcessedImageAsync(
            ProcessedImage image,
            string blobPathPrefix,
            CancellationToken cancellationToken = default);
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

        /// <summary>
        /// Replaces a stored blob's HTTP headers with a content type derived from its own bytes,
        /// discarding whatever the uploader set. No-op when the URL is not one of ours, storage is
        /// not configured, or the blob is gone.
        /// </summary>
        /// <remarks>
        /// The SAS content type is a response-header override that applies only to reads through
        /// that SAS. The stored content type — the one the anonymously readable public URL is
        /// served with — comes from the client's own PUT headers, so it cannot be trusted and is
        /// restamped once the bytes have been inspected.
        /// </remarks>
        Task NormalizeBlobHeadersAsync(
            string blobUrl,
            string contentType,
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
