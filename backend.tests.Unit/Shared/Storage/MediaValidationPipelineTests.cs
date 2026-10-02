using backend.main.shared.exceptions.http;
using backend.main.shared.storage;
using backend.main.shared.storage.imaging;

using backend.tests.Unit.Features.Media;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Shared.Storage;

public class MediaValidationPipelineTests
{
    private const string QuarantinePath = "events/clubs/1/pending/abc.png";
    private const string PublicUrl = InMemoryBlobStore.PublicBase + "/events/clubs/1/pending/abc.webp";

    [Fact]
    public async Task RunAsync_ShouldPublishTheProcessedImage_AndReportWhatWasStored()
    {
        var blobs = new InMemoryBlobStore();
        blobs.Put(QuarantinePath, InMemoryBlobStore.Image(width: 60, height: 20));
        var processed = new ProcessedImage([0x52, 0x49, 0x46, 0x46, 1, 2, 3], width: 60, height: 20);
        var processor = new Mock<IImageProcessor>();
        processor.Setup(p => p.ProcessAsync(It.IsAny<Stream>(), ImageProcessingProfile.Gallery, It.IsAny<CancellationToken>()))
            .ReturnsAsync(processed);

        var outcome = await new MediaValidationPipeline(blobs, processor.Object)
            .RunAsync(QuarantinePath, PublicUrl, "image/png", "Event images");

        outcome.Should().Be(MediaValidationOutcome.Accept("image/webp", 60, 20, 7));
        blobs.Published[PublicUrl].Should().BeSameAs(processed);
        blobs.Quarantine.Should().ContainKey(QuarantinePath, "the caller deletes it once the outcome is recorded");
    }

    [Fact]
    public async Task RunAsync_ShouldRejectBytesThatDisagreeWithTheDeclaredType_WithoutDecoding()
    {
        var blobs = new InMemoryBlobStore();
        blobs.Put(QuarantinePath, InMemoryBlobStore.Image("image/gif"));
        var processor = new Mock<IImageProcessor>(MockBehavior.Strict);

        var outcome = await new MediaValidationPipeline(blobs, processor.Object)
            .RunAsync(QuarantinePath, PublicUrl, "image/png", "Event images");

        outcome.Should().Be(MediaValidationOutcome.Reject(ImageUploadGate.MismatchMessage));
        blobs.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldRejectAMissingBlob_AsAnUploadThatDidNotComplete()
    {
        var outcome = await new MediaValidationPipeline(new InMemoryBlobStore(), Mock.Of<IImageProcessor>())
            .RunAsync(QuarantinePath, PublicUrl, "image/png", "Event images");

        outcome.RejectionReason.Should().Be(ImageUploadGate.DidNotCompleteMessage);
    }

    [Fact]
    public async Task RunAsync_ShouldRejectABlobThatVanishesBetweenInspectionAndDownload()
    {
        var blobs = new Mock<IAzureBlobService>();
        blobs.SetupGet(b => b.MaxImageBytes).Returns(1024);
        blobs.Setup(b => b.InspectQuarantineBlobAsync(QuarantinePath, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlobInspection(16, "image/png", FakePngHeader()));
        blobs.Setup(b => b.OpenQuarantineBlobReadAsync(QuarantinePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        var outcome = await new MediaValidationPipeline(blobs.Object, Mock.Of<IImageProcessor>())
            .RunAsync(QuarantinePath, PublicUrl, "image/png", "Event images");

        outcome.RejectionReason.Should().Be(ImageUploadGate.DidNotCompleteMessage);
    }

    [Fact]
    public async Task RunAsync_ShouldCapTheDownload_EvenWhenTheInspectedLengthWasSmall()
    {
        // The length check and the download are two requests. The SAS cannot overwrite, but the
        // pipeline does not rely on that: it never buffers more than the cap, whatever it reads.
        var blobs = new Mock<IAzureBlobService>();
        blobs.SetupGet(b => b.MaxImageBytes).Returns(1024);
        blobs.Setup(b => b.InspectQuarantineBlobAsync(QuarantinePath, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlobInspection(16, "image/png", FakePngHeader()));
        blobs.Setup(b => b.OpenQuarantineBlobReadAsync(QuarantinePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(new byte[4096]));
        var processor = new ImageSharpImageProcessor(Options.Create(new ImageProcessingOptions()));

        var outcome = await new MediaValidationPipeline(blobs.Object, processor)
            .RunAsync(QuarantinePath, PublicUrl, "image/png", "Event images");

        outcome.RejectionReason.Should().Be("Event images must be smaller than 1024 bytes.");
        blobs.Verify(
            b => b.UploadProcessedImageToAsync(It.IsAny<ProcessedImage>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_ShouldHandTheDownloadToTheProcessorUnbuffered()
    {
        // The processor buffers its input once, inside a processing slot. Buffering here first
        // would hold a whole upload per request while it queued for a slot.
        var blobs = new InMemoryBlobStore();
        blobs.Put(QuarantinePath, InMemoryBlobStore.Image());
        Stream? received = null;
        var processor = new Mock<IImageProcessor>();
        processor.Setup(p => p.ProcessAsync(It.IsAny<Stream>(), ImageProcessingProfile.Gallery, It.IsAny<CancellationToken>()))
            .Callback<Stream, ImageProcessingProfile, CancellationToken>((stream, _, _) => received = stream)
            .ReturnsAsync(new ProcessedImage([1, 2, 3]));

        await new MediaValidationPipeline(blobs, processor.Object)
            .RunAsync(QuarantinePath, PublicUrl, "image/png", "Event images");

        received.Should().NotBeNull();
        received.Should().NotBeOfType<MemoryStream>();
        received!.CanSeek.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ShouldLetAStorageFaultPropagate_RatherThanBlameTheUploader()
    {
        var blobs = new InMemoryBlobStore { FailNextPublish = new IOException("boom") };
        blobs.Put(QuarantinePath, InMemoryBlobStore.Image());
        var processor = new Mock<IImageProcessor>();
        processor.Setup(p => p.ProcessAsync(It.IsAny<Stream>(), ImageProcessingProfile.Gallery, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessedImage([1, 2, 3]));

        var act = () => new MediaValidationPipeline(blobs, processor.Object)
            .RunAsync(QuarantinePath, PublicUrl, "image/png", "Event images");

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task RunAsync_ShouldLetASlotTimeoutPropagate()
    {
        var blobs = new InMemoryBlobStore();
        blobs.Put(QuarantinePath, InMemoryBlobStore.Image());
        var processor = new Mock<IImageProcessor>();
        processor.Setup(p => p.ProcessAsync(It.IsAny<Stream>(), ImageProcessingProfile.Gallery, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotAvailableException("busy"));

        var act = () => new MediaValidationPipeline(blobs, processor.Object)
            .RunAsync(QuarantinePath, PublicUrl, "image/png", "Event images");

        await act.Should().ThrowAsync<NotAvailableException>();
    }

    private static byte[] FakePngHeader() =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
}
