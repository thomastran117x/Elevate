using backend.main.features.events.contracts.responses;
using backend.main.shared.storage;

namespace backend.tests.Integration.Infrastructure;

public sealed class FakeAzureBlobService : IAzureBlobService
{
    private const string BaseUrl = "https://storage.test/event-assets";
    private readonly HashSet<string> _ownedUrls = [];

    public Task<PresignedUploadResponse> GenerateUploadUrlAsync(string blobPathPrefix, string fileName, string contentType)
    {
        var safePrefix = blobPathPrefix.Trim('/').Replace(' ', '-');
        var safeFileName = Path.GetFileName(fileName);
        var publicUrl = $"{BaseUrl}/{safePrefix}/{Guid.NewGuid():N}-{safeFileName}";
        _ownedUrls.Add(publicUrl);

        return Task.FromResult(new PresignedUploadResponse
        {
            UploadUrl = $"{publicUrl}?signature=test-upload",
            PublicUrl = publicUrl,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        });
    }

    public bool IsOwnedBlobUrl(string blobUrl) => _ownedUrls.Contains(blobUrl);

    public Task DeleteBlobAsync(string blobUrl)
    {
        _ownedUrls.Remove(blobUrl);
        return Task.CompletedTask;
    }
}
