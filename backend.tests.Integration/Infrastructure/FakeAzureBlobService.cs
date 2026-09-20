using System.Runtime.CompilerServices;

using backend.main.features.events.contracts.responses;
using backend.main.shared.storage;

namespace backend.tests.Integration.Infrastructure;

/// <summary>
/// What the fake reports when a blob is inspected: the metadata a test wants an attach path to
/// see, standing in for bytes that a real client would have PUT to the presigned URL.
/// </summary>
public sealed record StagedBlob(long ContentLength, string? ContentType, byte[] HeaderBytes);

public sealed class FakeAzureBlobService : IAzureBlobService
{
    private const string BaseUrl = "https://storage.test/event-assets";
    private const long DefaultStagedLength = 1024;

    private readonly Dictionary<string, DateTimeOffset> _ownedUrls = [];
    private readonly Dictionary<string, StagedBlob> _stagedBlobs = [];
    private readonly List<string> _inspectedUrls = [];

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

    public void Clear()
    {
        _ownedUrls.Clear();
        _stagedBlobs.Clear();
        _inspectedUrls.Clear();
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

    public Task<string> UploadImageAsync(IFormFile image, string blobPathPrefix)
    {
        return Task.FromResult(CreateOwnedBlobUrl(
            blobPathPrefix,
            image.FileName,
            DateTimeOffset.UtcNow,
            image.ContentType));
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
