using System.Runtime.CompilerServices;

using backend.main.features.events.contracts.responses;
using backend.main.shared.storage;

namespace backend.tests.Integration.Infrastructure;

public sealed class FakeAzureBlobService : IAzureBlobService
{
    private const string BaseUrl = "https://storage.test/event-assets";
    private readonly Dictionary<string, DateTimeOffset> _ownedUrls = [];

    public string CreateOwnedBlobUrl(string blobPathPrefix, string fileName)
        => CreateOwnedBlobUrl(blobPathPrefix, fileName, DateTimeOffset.UtcNow);

    public string CreateOwnedBlobUrl(string blobPathPrefix, string fileName, DateTimeOffset lastModified)
    {
        var safePrefix = string.Join(
            '/',
            blobPathPrefix
                .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(segment => segment is not "." and not ".."));
        var safeFileName = Path.GetFileName(fileName);
        var publicUrl = $"{BaseUrl}/{safePrefix}/{Guid.NewGuid():N}-{safeFileName}";
        _ownedUrls[publicUrl] = lastModified;
        return publicUrl;
    }

    public Task<string> UploadImageAsync(IFormFile image, string blobPathPrefix)
    {
        return Task.FromResult(CreateOwnedBlobUrl(blobPathPrefix, image.FileName));
    }

    public Task<PresignedUploadResponse> GenerateUploadUrlAsync(string blobPathPrefix, string fileName, string contentType)
    {
        var publicUrl = CreateOwnedBlobUrl(blobPathPrefix.Replace(' ', '-'), fileName);

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
}
