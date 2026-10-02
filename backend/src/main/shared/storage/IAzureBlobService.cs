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

        /// <summary>
        /// Stores a processed image at a public URL this service reserved earlier with
        /// <see cref="GenerateQuarantineUploadUrlAsync"/>, overwriting a previous attempt.
        /// </summary>
        /// <exception cref="ArgumentException">The URL is not in the public container.</exception>
        Task UploadProcessedImageToAsync(
            ProcessedImage image,
            string publicUrl,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Mints a write-once SAS straight into the public container. Used only while
        /// <c>storage.quarantine</c> is off; with it on, uploads go through
        /// <see cref="GenerateQuarantineUploadUrlAsync"/> instead.
        /// </summary>
        Task<PresignedUploadResponse> GenerateUploadUrlAsync(string blobPathPrefix, string fileName, string contentType);

        /// <summary>
        /// Mints a write-once SAS into the private quarantine container, and reserves the public
        /// URL the image will be published at once it has been validated and re-encoded. Nothing
        /// exists at that public URL until then.
        /// </summary>
        Task<QuarantineUpload> GenerateQuarantineUploadUrlAsync(string blobPathPrefix, string fileName, string contentType);
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

        /// <summary>
        /// <see cref="InspectBlobAsync"/> for a blob in the quarantine container, by name.
        /// Returns null when it does not exist or storage is not configured.
        /// </summary>
        Task<BlobInspection?> InspectQuarantineBlobAsync(
            string quarantineBlobPath,
            int prefixByteCount = ImageSignatureInspector.HeaderByteCount,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a quarantined blob for reading, or returns null when it does not exist. The
        /// caller owns the stream and bounds how much of it is read.
        /// </summary>
        Task<Stream?> OpenQuarantineBlobReadAsync(
            string quarantineBlobPath,
            CancellationToken cancellationToken = default);

        /// <summary>Best-effort delete from the quarantine container; never throws.</summary>
        Task DeleteQuarantineBlobAsync(string quarantineBlobPath);

        /// <summary>
        /// Every blob in the quarantine container, by name. Yields nothing when storage is not
        /// configured.
        /// </summary>
        IAsyncEnumerable<QuarantineBlobItem> ListQuarantineBlobsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// The names of the containers this service needs that do not exist: the public one, and
        /// the quarantine one when <paramref name="includeQuarantine"/>. Empty when storage is
        /// not configured, which is reported elsewhere. Needs read access only; nothing is
        /// created.
        /// </summary>
        Task<IReadOnlyList<string>> FindMissingContainersAsync(
            bool includeQuarantine,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A presigned upload into quarantine: where the client PUTs, where the bytes land, and the
    /// public URL reserved for the validated result.
    /// </summary>
    public sealed record QuarantineUpload(
        string UploadUrl,
        string QuarantineBlobPath,
        string PublicUrl,
        string ContentType,
        DateTimeOffset ExpiresAt);

    public readonly record struct QuarantineBlobItem(string Path, DateTimeOffset? LastModified);

    public readonly record struct BlobListItem(string Url, DateTimeOffset? LastModified);

    /// <summary>
    /// What a stored blob turned out to be, as opposed to what its uploader declared.
    /// </summary>
    public readonly record struct BlobInspection(
        long ContentLength,
        string? ContentType,
        byte[] HeaderBytes);
}
