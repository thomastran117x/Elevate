using System.Runtime.CompilerServices;

using backend.main.features.events.contracts.responses;
using backend.main.shared.storage;
using backend.main.shared.storage.imaging;

namespace backend.tests.Integration.Infrastructure;

/// <summary>
/// What the fake reports when a blob is inspected: the metadata a test wants an attach path to
/// see, standing in for bytes that a real client would have PUT to the presigned URL.
/// </summary>
/// <remarks>
/// The trailing headers are the rest of the group Set Blob Properties writes together. A client
/// sets them on its own PUT, so a test can plant hostile values and check they are gone after
/// the attach rewrites the header set.
/// </remarks>
public sealed record StagedBlob(
    long ContentLength,
    string? ContentType,
    byte[] HeaderBytes,
    string? ContentDisposition = null,
    string? CacheControl = null,
    string? ContentEncoding = null,
    string? ContentLanguage = null);

public sealed class FakeAzureBlobService : IAzureBlobService
{
    private const string BaseUrl = "https://storage.test/event-assets";
    private const long DefaultStagedLength = 1024;

    private readonly Dictionary<string, DateTimeOffset> _ownedUrls = [];
    private readonly Dictionary<string, StagedBlob> _stagedBlobs = [];
    private readonly List<string> _inspectedUrls = [];
    private readonly Dictionary<string, string> _normalizedContentTypes = [];
    private readonly Dictionary<string, ProcessedImage> _uploadedImages = [];

    public long MaxImageBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Blob metadata by public URL. Minting a URL stages a valid image for it, so tests that only
    /// care about ownership keep working; a test that wants an attach to fail overwrites the
    /// entry with an oversized or non-image blob.
    /// </summary>
    public IDictionary<string, StagedBlob> StagedBlobs => _stagedBlobs;

    /// <summary>
    /// Every URL <see cref="InspectBlobAsync"/> was called for, in order. Lets a test assert that
    /// re-attaching an already-attached URL short-circuits without a storage round trip.
    /// </summary>
    public IReadOnlyList<string> InspectedUrls => _inspectedUrls;

    /// <summary>
    /// The content type each blob's headers were rewritten with when it was attached. The stored
    /// headers come from the uploader's own PUT, so the server replaces them from the bytes.
    /// </summary>
    public IReadOnlyDictionary<string, string> NormalizedContentTypes => _normalizedContentTypes;

    /// <summary>
    /// The processed bytes stored by <see cref="UploadProcessedImageAsync"/>, by public URL, so a
    /// test can decode what would have been published and check nothing but pixels survived.
    /// </summary>
    public IReadOnlyDictionary<string, ProcessedImage> UploadedImages => _uploadedImages;

    public void Clear()
    {
        _ownedUrls.Clear();
        _uploadedImages.Clear();
        _stagedBlobs.Clear();
        _inspectedUrls.Clear();
        _normalizedContentTypes.Clear();
    }

    /// <summary>
    /// Builds the leading bytes of a real file of the given type, long enough for every signature
    /// the inspector knows about.
    /// </summary>
    public static byte[] HeaderFor(string? contentType)
    {
        var header = new byte[ImageSignatureInspector.HeaderByteCount];

        switch ((contentType ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "image/jpeg":
            case "image/jpg":
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(header, 0);
                break;
            case "image/gif":
                "GIF89a"u8.ToArray().CopyTo(header, 0);
                break;
            case "image/webp":
                "RIFF"u8.ToArray().CopyTo(header, 0);
                "WEBP"u8.ToArray().CopyTo(header, 8);
                break;
            default:
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(header, 0);
                break;
        }

        return header;
    }

    /// <summary>
    /// A blob that passes every attach-time check, for the given type and size.
    /// </summary>
    public static StagedBlob ImageBlob(string contentType = "image/png", long contentLength = DefaultStagedLength) =>
        new(contentLength, contentType, HeaderFor(contentType));

    public string CreateOwnedBlobUrl(string blobPathPrefix, string fileName)
        => CreateOwnedBlobUrl(blobPathPrefix, fileName, DateTimeOffset.UtcNow);

    public string CreateOwnedBlobUrl(
        string blobPathPrefix,
        string fileName,
        DateTimeOffset lastModified,
        string? contentType = null)
    {
        var safePrefix = string.Join(
            '/',
            blobPathPrefix
                .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(segment => segment is not "." and not ".."));
        var safeFileName = Path.GetFileName(fileName);
        var publicUrl = $"{BaseUrl}/{safePrefix}/{Guid.NewGuid():N}-{safeFileName}";
        _ownedUrls[publicUrl] = lastModified;
        _stagedBlobs[publicUrl] = ImageBlob(ResolveContentType(contentType, safeFileName));
        return publicUrl;
    }

    /// <remarks>
    /// Mirrors the real service's naming: the stored name takes its extension from the processed
    /// output, so every avatar ends in .webp whatever the uploader called the file.
    /// </remarks>
    public Task<string> UploadProcessedImageAsync(
        ProcessedImage image,
        string blobPathPrefix,
        CancellationToken cancellationToken = default)
    {
        var url = CreateOwnedBlobUrl(
            blobPathPrefix,
            "upload" + image.FileExtension,
            DateTimeOffset.UtcNow,
            image.ContentType);
        _uploadedImages[url] = image;
        return Task.FromResult(url);
    }

    public Task<PresignedUploadResponse> GenerateUploadUrlAsync(string blobPathPrefix, string fileName, string contentType)
    {
        var publicUrl = CreateOwnedBlobUrl(
            blobPathPrefix.Replace(' ', '-'),
            fileName,
            DateTimeOffset.UtcNow,
            contentType);

        return Task.FromResult(new PresignedUploadResponse
        {
            UploadUrl = $"{publicUrl}?signature=test-upload",
            PublicUrl = publicUrl,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        });
    }

    public bool IsOwnedBlobUrl(string blobUrl) => _ownedUrls.ContainsKey(blobUrl);

    public Task DeleteBlobAsync(string blobUrl)
    {
        _ownedUrls.Remove(blobUrl);
        _stagedBlobs.Remove(blobUrl);
        return Task.CompletedTask;
    }

    public Task<BlobInspection?> InspectBlobAsync(
        string blobUrl,
        int prefixByteCount = ImageSignatureInspector.HeaderByteCount,
        CancellationToken cancellationToken = default)
    {
        _inspectedUrls.Add(blobUrl);

        if (!_ownedUrls.ContainsKey(blobUrl) || !_stagedBlobs.TryGetValue(blobUrl, out var staged))
            return Task.FromResult<BlobInspection?>(null);

        var header = staged.HeaderBytes.Length <= prefixByteCount
            ? staged.HeaderBytes
            : staged.HeaderBytes[..prefixByteCount];

        return Task.FromResult<BlobInspection?>(
            new BlobInspection(staged.ContentLength, staged.ContentType, header));
    }

    public Task NormalizeBlobHeadersAsync(
        string blobUrl,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        _normalizedContentTypes[blobUrl] = contentType;

        // Mirrors Set Blob Properties: the header group is set together, and every member the
        // request leaves out is cleared.
        if (_stagedBlobs.TryGetValue(blobUrl, out var staged))
        {
            _stagedBlobs[blobUrl] = staged with
            {
                ContentType = contentType,
                ContentDisposition = null,
                CacheControl = null,
                ContentEncoding = null,
                ContentLanguage = null
            };
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<BlobListItem> ListBlobsAsync(
        string blobPathPrefix,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prefix = $"{BaseUrl}/{blobPathPrefix.Trim('/')}/";
        foreach (var entry in _ownedUrls.ToList())
        {
            if (entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                yield return new BlobListItem(entry.Key, entry.Value);
        }

        await Task.CompletedTask;
    }

    /// <remarks>
    /// Mirrors the real service: the caller's content type wins, but the browser sends
    /// "application/octet-stream" when it cannot determine a file's type, and the extension
    /// decides from there.
    /// </remarks>
    private static string ResolveContentType(string? contentType, string fileName)
    {
        var normalized = (contentType ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(normalized) && normalized != "application/octet-stream")
            return normalized;

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }
}
