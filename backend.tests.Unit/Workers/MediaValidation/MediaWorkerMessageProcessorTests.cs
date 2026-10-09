using backend.main.shared.providers.messages;
using backend.main.shared.storage;
using backend.main.shared.storage.imaging;
using backend.tests.Unit.Features.Media;
using backend.tests.Unit.Support;
using backend.worker.media_worker;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Workers.MediaValidation;

public class MediaWorkerMessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldPublishTheReencodedImage_AndReportItAccepted()
    {
        var harness = new Harness();
        harness.Blobs.Put(MediaWorkerTestMessages.QuarantinePath, InMemoryBlobStore.Image("image/png", 60, 40));
        var request = MediaWorkerTestMessages.Request(attempt: 2);

        await harness.Processor.ProcessAsync(MediaWorkerTestMessages.Envelope(request));

        var published = harness.Blobs.Published[request.PublicUrl];
        published.ContentType.Should().Be("image/webp");
        published.Width.Should().Be(60);
        TestImages.MetadataFields(published.Content).Should().BeEmpty();

        var result = harness.Results.Should().ContainSingle().Subject;
        result.MediaAssetId.Should().Be(request.MediaAssetId);
        result.Attempt.Should().Be(2, "the API records a result only under the claim it was requested for");
        result.Accepted.Should().BeTrue();
        result.ContentType.Should().Be("image/webp");
        result.Width.Should().Be(60);
        result.Height.Should().Be(40);
        result.ByteSize.Should().Be(published.Content.LongLength);
        harness.DlqErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_ShouldReportARejection_WithoutPublishingAnything()
    {
        var harness = new Harness();
        harness.Blobs.Put(MediaWorkerTestMessages.QuarantinePath, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00]);

        await harness.Processor.ProcessAsync(MediaWorkerTestMessages.Envelope(MediaWorkerTestMessages.Request()));

        harness.Blobs.Published.Should().BeEmpty();
        var result = harness.Results.Should().ContainSingle().Subject;
        result.Accepted.Should().BeFalse();
        result.RejectionReason.Should().Be(ImageUploadGate.UnsupportedFormatMessage);
        harness.DlqErrors.Should().BeEmpty("a refused image is an outcome, not a failure");
    }

    [Fact]
    public async Task ProcessAsync_ShouldNamePromotedUploadsWithTheRequestsSubject()
    {
        var harness = new Harness();
        harness.Blobs.MaxImageBytes = 64;
        harness.Blobs.Put(MediaWorkerTestMessages.QuarantinePath, InMemoryBlobStore.Image());

        await harness.Processor.ProcessAsync(
            MediaWorkerTestMessages.Envelope(MediaWorkerTestMessages.Request(subject: "Club images")));

        harness.Results.Should().ContainSingle()
            .Which.RejectionReason.Should().Be("Club images must be smaller than 64 bytes.");
    }

    [Fact]
    public async Task ProcessAsync_ShouldNotPublishASecondTime_WhenTheSameRequestIsRedelivered()
    {
        // Kafka redelivers. By the time a duplicate arrives the API has recorded the first result
        // and deleted the quarantined bytes, so the replay finds nothing to promote — and the
        // "did not complete" it reports is ignored, because the asset is no longer Processing.
        var harness = new Harness();
        harness.Blobs.Put(MediaWorkerTestMessages.QuarantinePath, InMemoryBlobStore.Image());
        var envelope = MediaWorkerTestMessages.Envelope(MediaWorkerTestMessages.Request());
        await harness.Processor.ProcessAsync(envelope);
        await harness.Blobs.DeleteQuarantineBlobAsync(MediaWorkerTestMessages.QuarantinePath);
        harness.Blobs.Published.Clear();

        await harness.Processor.ProcessAsync(envelope);

        harness.Blobs.Published.Should().BeEmpty("a replay must not write the public URL again");
        harness.Results.Should().HaveCount(2);
        harness.Results[1].Accepted.Should().BeFalse();
        harness.Results[1].RejectionReason.Should().Be(ImageUploadGate.DidNotCompleteMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRetryATransientFault_AndThenReportTheOutcome()
    {
        var harness = new Harness();
        harness.Blobs.Put(MediaWorkerTestMessages.QuarantinePath, InMemoryBlobStore.Image());
        harness.Blobs.FailNextPublish = new IOException("storage blipped");

        await harness.Processor.ProcessAsync(MediaWorkerTestMessages.Envelope(MediaWorkerTestMessages.Request()));

        harness.Results.Should().ContainSingle().Which.Accepted.Should().BeTrue();
        harness.DlqErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_ShouldDeadLetter_WhenTheFaultOutlastsTheRetries()
    {
        var harness = new Harness();
        harness.Blobs.Put(MediaWorkerTestMessages.QuarantinePath, InMemoryBlobStore.Image());
        harness.Blobs.OnPublish = () => throw new IOException("storage is down");

        await harness.Processor.ProcessAsync(MediaWorkerTestMessages.Envelope(MediaWorkerTestMessages.Request()));

        harness.DlqErrors.Should().ContainSingle().Which.Should().Be("storage is down");
        harness.Results.Should().BeEmpty("the asset stays Processing for the reconciler to re-drive");
    }

    [Fact]
    public async Task ProcessAsync_ShouldDeadLetterAMalformedRequest_WithoutTouchingStorage()
    {
        var harness = new Harness();

        await harness.Processor.ProcessAsync(MediaWorkerTestMessages.Envelope("{not json"));

        harness.DlqErrors.Should().ContainSingle().Which.Should().Contain("not valid JSON");
        harness.Blobs.QuarantineInspections.Should().Be(0);
        harness.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_ShouldLetAFailedReportPropagate_SoTheOffsetIsNotCommitted()
    {
        var harness = new Harness();
        harness.Blobs.Put(MediaWorkerTestMessages.QuarantinePath, InMemoryBlobStore.Image());
        harness.StatusPublisher
            .Setup(p => p.PublishAsync(It.IsAny<MediaValidationResultMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"));

        var act = () => harness.Processor.ProcessAsync(MediaWorkerTestMessages.Envelope(MediaWorkerTestMessages.Request()));

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.DlqErrors.Should().BeEmpty("the request is redelivered instead");
    }

    [Fact]
    public async Task ProcessAsync_ShouldRethrowCancellation()
    {
        var harness = new Harness();
        harness.Blobs.Put(MediaWorkerTestMessages.QuarantinePath, InMemoryBlobStore.Image());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => harness.Processor.ProcessAsync(
            MediaWorkerTestMessages.Envelope(MediaWorkerTestMessages.Request()), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.DlqErrors.Should().BeEmpty();
        harness.Results.Should().BeEmpty();
    }

    private sealed class Harness
    {
        public Harness()
        {
            DlqPublisher
                .Setup(p => p.PublishAsync(It.IsAny<MediaWorkerEnvelope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<MediaWorkerEnvelope, string, CancellationToken>((_, error, _) => DlqErrors.Add(error))
                .Returns(Task.CompletedTask);
            StatusPublisher
                .Setup(p => p.PublishAsync(It.IsAny<MediaValidationResultMessage>(), It.IsAny<CancellationToken>()))
                .Callback<MediaValidationResultMessage, CancellationToken>((message, _) => Results.Add(message))
                .Returns(Task.CompletedTask);

            Processor = new MediaWorkerMessageProcessor(
                new MediaValidationPipeline(
                    Blobs,
                    new NetVipsImageProcessor(Options.Create(new ImageProcessingOptions()))),
                DlqPublisher.Object,
                StatusPublisher.Object);
        }

        public InMemoryBlobStore Blobs { get; } = new();
        public Mock<IMediaWorkerDlqPublisher> DlqPublisher { get; } = new();
        public Mock<IMediaValidationStatusPublisher> StatusPublisher { get; } = new();
        public List<string> DlqErrors { get; } = [];
        public List<MediaValidationResultMessage> Results { get; } = [];
        public MediaWorkerMessageProcessor Processor { get; }
    }
}
