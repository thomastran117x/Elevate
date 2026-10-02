using System.Runtime.CompilerServices;

using backend.main.features.events.contracts.responses;
using backend.main.shared.storage;
using backend.main.shared.storage.imaging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace backend.tests.Unit.Features.Media;

/// <summary>
/// Both containers held in memory with whole files, so the media tests can run the real image
/// processor over what a client "uploaded" and inspect exactly what was published.
/// </summary>
internal sealed class InMemoryBlobStore : IAzureBlobService
{
    public const string PublicBase = "https://storage.test/public";

    public Dictionary<string, (byte[] Content, string ContentType, DateTimeOffset LastModified)> Quarantine { get; } = [];

    public Dictionary<string, ProcessedImage> Published { get; } = [];

    /// <summary>Public-container blobs a flag-off upload wrote directly, by URL.</summary>
    public Dictionary<string, byte[]> PublicUploads { get; } = [];

    public List<string> DeletedQuarantinePaths { get; } = [];

    public int QuarantineInspections
    {
        get; private set;
    }

    public long MaxImageBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Thrown from the next public write, to simulate a storage fault mid-promotion.</summary>
    public Exception? FailNextPublish
    {
        get; set;
    }

    /// <summary>Runs inside every quarantine inspection, to interleave another request.</summary>
    public Func<Task>? OnQuarantineInspect
    {
        get; set;
    }

    /// <summary>Runs inside every public write, before it lands, to interleave another request.</summary>
    public Func<Task>? OnPublish
    {
        get; set;
    }

    /// <summary>Thrown from quarantine listing, to simulate an unprovisioned container.</summary>
    public Exception? FailListing
    {
        get; set;
    }

    public Task<QuarantineUpload> GenerateQuarantineUploadUrlAsync(string blobPathPrefix, string fileName, string contentType)
    {
        var id = Guid.NewGuid().ToString("N");
        var path = $"{blobPathPrefix}/{id}{Path.GetExtension(fileName).ToLowerInvariant()}";
        return Task.FromResult(new QuarantineUpload(
            $"https://storage.test/quarantine/{path}?sig=test",
            path,
            $"{PublicBase}/{blobPathPrefix}/{id}.webp",
            contentType,
            DateTimeOffset.UtcNow.AddMinutes(15)));
    }

    public Task<PresignedUploadResponse> GenerateUploadUrlAsync(string blobPathPrefix, string fileName, string contentType)
    {
        var url = $"{PublicBase}/{blobPathPrefix}/{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        return Task.FromResult(new PresignedUploadResponse
        {
            UploadUrl = url + "?sig=test",
            PublicUrl = url,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        });
    }

    public void Put(string quarantinePath, byte[] content, string contentType = "image/png", DateTimeOffset? lastModified = null) =>
        Quarantine[quarantinePath] = (content, contentType, lastModified ?? DateTimeOffset.UtcNow);

    public bool IsOwnedBlobUrl(string blobUrl) => blobUrl.StartsWith(PublicBase + "/", StringComparison.Ordinal);

    public async Task<BlobInspection?> InspectQuarantineBlobAsync(
        string quarantineBlobPath,
        int prefixByteCount = ImageSignatureInspector.HeaderByteCount,
        CancellationToken cancellationToken = default)
    {
        QuarantineInspections++;
        if (OnQuarantineInspect is { } hook)
            await hook();

        if (!Quarantine.TryGetValue(quarantineBlobPath, out var blob))
            return null;

        var header = blob.Content.Length <= prefixByteCount ? blob.Content : blob.Content[..prefixByteCount];
        return new BlobInspection(blob.Content.LongLength, blob.ContentType, header);
    }

    public Task<Stream?> OpenQuarantineBlobReadAsync(string quarantineBlobPath, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(Quarantine.TryGetValue(quarantineBlobPath, out var blob)
            ? new MemoryStream(blob.Content, writable: false)
            : null);

    public async Task UploadProcessedImageToAsync(ProcessedImage image, string publicUrl, CancellationToken cancellationToken = default)
    {
        if (OnPublish is { } hook)
            await hook();

        if (FailNextPublish is { } failure)
        {
            FailNextPublish = null;
            throw failure;
        }

        if (!IsOwnedBlobUrl(publicUrl))
            throw new ArgumentException("The target URL is not in the public container.", nameof(publicUrl));

        Published[publicUrl] = image;
    }

    public Task DeleteQuarantineBlobAsync(string quarantineBlobPath)
    {
        DeletedQuarantinePaths.Add(quarantineBlobPath);
        Quarantine.Remove(quarantineBlobPath);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<QuarantineBlobItem> ListQuarantineBlobsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (FailListing is { } failure)
            throw failure;

        foreach (var entry in Quarantine.ToList())
            yield return new QuarantineBlobItem(entry.Key, entry.Value.LastModified);

        await Task.CompletedTask;
    }

    // The public-container surface a flag-off attach uses.
    public Task<BlobInspection?> InspectBlobAsync(
        string blobUrl,
        int prefixByteCount = ImageSignatureInspector.HeaderByteCount,
        CancellationToken cancellationToken = default)
    {
        if (!PublicUploads.TryGetValue(blobUrl, out var content))
            return Task.FromResult<BlobInspection?>(null);

        var header = content.Length <= prefixByteCount ? content : content[..prefixByteCount];
        return Task.FromResult<BlobInspection?>(new BlobInspection(content.LongLength, "image/png", header));
    }

    public Task NormalizeBlobHeadersAsync(string blobUrl, string contentType, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task DeleteBlobAsync(string blobUrl)
    {
        PublicUploads.Remove(blobUrl);
        return Task.CompletedTask;
    }

    public Task<string> UploadProcessedImageAsync(ProcessedImage image, string blobPathPrefix, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<BlobListItem> ListBlobsAsync(
        string blobPathPrefix,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>A real, decodable image of the given type.</summary>
    public static byte[] Image(string contentType = "image/png", int width = 40, int height = 30, int frames = 1)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(30, 90, 150));
        for (var i = 1; i < frames; i++)
        {
            using var frame = new Image<Rgba32>(width, height, new Rgba32(200, 20, 20));
            image.Frames.AddFrame(frame.Frames.RootFrame);
        }

        using var stream = new MemoryStream();
        switch (contentType)
        {
            case "image/jpeg":
                image.SaveAsJpeg(stream);
                break;
            case "image/gif":
                image.SaveAsGif(stream);
                break;
            case "image/webp":
                image.Save(stream, new WebpEncoder());
                break;
            default:
                image.SaveAsPng(stream);
                break;
        }

        return stream.ToArray();
    }
}
