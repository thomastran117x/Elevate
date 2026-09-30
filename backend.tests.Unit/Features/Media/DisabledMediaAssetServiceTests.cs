using System.Text.Json;

using backend.main.application.features;
using backend.main.features.cache;
using backend.main.features.media;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Media;

/// <summary>
/// With <c>storage.quarantine</c> off, uploads must behave exactly as they did before quarantine
/// existed: presigned straight into the public container, checked in place at attach.
/// </summary>
public class DisabledMediaAssetServiceTests
{
    [Fact]
    public async Task IssueUploadAsync_ShouldPresignIntoThePublicContainer_WithNoMediaAsset()
    {
        var blobs = new InMemoryBlobStore();
        var service = new DisabledMediaAssetService(blobs, Mock.Of<ICacheService>());

        var upload = await service.IssueUploadAsync(
            new MediaUploadRequest(7, 3, null, "events/clubs/3/pending", "poster.png", "image/png"));

        upload.MediaAssetId.Should().BeNull();
        upload.PublicUrl.Should().StartWith(InMemoryBlobStore.PublicBase + "/events/clubs/3/pending/").And.EndWith(".png");
        upload.UploadUrl.Should().StartWith(upload.PublicUrl);
    }

    [Fact]
    public async Task AttachAsync_ShouldCheckTheBytesInThePublicContainer_ThenRunTheScopeCheck()
    {
        var blobs = new InMemoryBlobStore();
        var url = $"{InMemoryBlobStore.PublicBase}/events/poster.png";
        blobs.PublicUploads[url] = InMemoryBlobStore.Image();
        var service = new DisabledMediaAssetService(blobs, CacheWith(url, userId: 7));
        BlobUploadIntent? scoped = null;

        var intent = await service.AttachAsync(7, url, "Event images", i => scoped = i);

        intent.PublicUrl.Should().Be(url);
        scoped.Should().Be(intent);
        blobs.QuarantineInspections.Should().Be(0);
    }

    [Fact]
    public async Task AttachAsync_ShouldRejectAndDeleteBadBytes_BeforeTheScopeCheck()
    {
        var blobs = new InMemoryBlobStore();
        var url = $"{InMemoryBlobStore.PublicBase}/events/poster.png";
        blobs.PublicUploads[url] = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00];
        var service = new DisabledMediaAssetService(blobs, CacheWith(url, userId: 7));
        var scopeChecked = false;

        var act = () => service.AttachAsync(7, url, "Event images", _ => scopeChecked = true);

        await act.Should().ThrowAsync<BadRequestException>().WithMessage(ImageUploadGate.UnsupportedFormatMessage);
        scopeChecked.Should().BeFalse("the pre-quarantine order checked bytes first");
        blobs.PublicUploads.Should().NotContainKey(url);
    }

    private static ICacheService CacheWith(string url, int userId)
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync(BlobUploadIntentValidator.IntentKey(url)))
            .ReturnsAsync(JsonSerializer.Serialize(new BlobUploadIntent(1, null, userId, url, "image/png")));
        return cache.Object;
    }
}
