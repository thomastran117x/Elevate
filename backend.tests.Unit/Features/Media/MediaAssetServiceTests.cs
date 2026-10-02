using System.Text.Json;

using backend.main.features.cache;
using backend.main.features.events.contracts.responses;
using backend.main.features.media;
using backend.main.shared.exceptions.http;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.storage;
using backend.main.shared.storage.imaging;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Moq;

using SixLabors.ImageSharp;

namespace backend.tests.Unit.Features.Media;

public class MediaAssetServiceTests
{
    [Fact]
    public async Task IssueUploadAsync_ShouldRecordAPendingAsset_AndReturnItsId()
    {
        await using var harness = await Harness.CreateAsync();

        var upload = await harness.Service.IssueUploadAsync(
            new MediaUploadRequest(harness.UserId, null, null, "clubs/pending/1", "photo.png", "image/png"));

        upload.MediaAssetId.Should().NotBeNull();
        upload.PublicUrl.Should().EndWith(".webp");
        upload.UploadUrl.Should().Contain("/quarantine/");

        var asset = await harness.AssetAsync(upload);
        asset.Status.Should().Be(MediaAssetStatus.PendingUpload);
        asset.Origin.Should().Be(MediaAssetOrigin.Upload);
        asset.OwnerUserId.Should().Be(harness.UserId);
        asset.PublicUrl.Should().Be(upload.PublicUrl);
        asset.QuarantineBlobPath.Should().StartWith("clubs/pending/1/").And.EndWith(".png");
        asset.DeclaredContentType.Should().Be("image/png");
        asset.ValidatedAt.Should().BeNull();
        harness.Blobs.Published.Should().BeEmpty("nothing is public until it has been validated");
    }

    [Fact]
    public async Task AttachAsync_ShouldPublishAReencodedImage_AndMarkTheAssetReady()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image("image/jpeg", 3000, 1500), "image/jpeg");
        var quarantinePath = (await harness.AssetAsync(upload)).QuarantineBlobPath!;

        var intent = await harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        intent.MediaAssetId.Should().Be(upload.MediaAssetId);
        var published = harness.Blobs.Published[upload.PublicUrl];
        published.ContentType.Should().Be("image/webp");
        var stored = Image.Identify(published.Content);
        stored.Width.Should().Be(2048, "gallery uploads are capped at the gallery edge, not the avatar one");
        stored.Height.Should().Be(1024);
        stored.Metadata.ExifProfile.Should().BeNull();

        var asset = await harness.AssetAsync(upload);
        asset.Status.Should().Be(MediaAssetStatus.Ready);
        asset.ContentType.Should().Be("image/webp");
        asset.Width.Should().Be(2048);
        asset.Height.Should().Be(1024);
        asset.ByteSize.Should().Be(published.Content.LongLength);
        asset.ValidatedAt.Should().NotBeNull();
        asset.AttemptCount.Should().Be(1);
        asset.QuarantineBlobPath.Should().BeNull();
        harness.Blobs.Quarantine.Should().NotContainKey(quarantinePath);
    }

    [Fact]
    public async Task AttachAsync_ShouldRejectBytesThatAreNotAnImage_AndLeaveNothingInEitherContainer()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00]);
        var quarantinePath = (await harness.AssetAsync(upload)).QuarantineBlobPath!;

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ImageUploadGate.UnsupportedFormatMessage);
        harness.Blobs.Published.Should().BeEmpty();
        harness.Blobs.Quarantine.Should().NotContainKey(quarantinePath);

        var asset = await harness.AssetAsync(upload);
        asset.Status.Should().Be(MediaAssetStatus.Rejected);
        asset.RejectionReason.Should().Be(ImageUploadGate.UnsupportedFormatMessage);
        asset.QuarantineBlobPath.Should().BeNull();
    }

    [Fact]
    public async Task AttachAsync_ShouldRejectAnAnimatedImage_WithTheProcessorsReason()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image("image/gif", frames: 2), "image/gif");

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ImageSharpImageProcessor.AnimatedMessage);
        (await harness.AssetAsync(upload)).RejectionReason.Should().Be(ImageSharpImageProcessor.AnimatedMessage);
    }

    [Fact]
    public async Task AttachAsync_ShouldSayTheUploadDidNotComplete_AndStayPending_WhenNothingWasUploaded()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAsync();

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ImageUploadGate.DidNotCompleteMessage);
        (await harness.AssetAsync(upload)).Status.Should().Be(MediaAssetStatus.PendingUpload,
            "a PUT retried within the SAS window can still complete it");
    }

    [Fact]
    public async Task AttachAsync_ShouldBeANoOp_ForAnAlreadyReadyAsset()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        await harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");
        harness.Blobs.Published.Clear();

        await harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        harness.Blobs.Published.Should().BeEmpty("a second attach must not run the pipeline again");
        (await harness.AssetAsync(upload)).AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task AttachAsync_ShouldRepeatTheStoredReason_ForAnAlreadyRejectedAsset()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C]);
        await FluentActions.Awaiting(() => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images"))
            .Should().ThrowAsync<BadRequestException>();

        var again = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await again.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ImageUploadGate.UnsupportedFormatMessage);
    }

    [Fact]
    public async Task AttachAsync_ShouldReportStillProcessing_WhileAnotherAttachHoldsAFreshClaim()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        var asset = await harness.AssetAsync(upload);
        await harness.Repository.TryTransitionAsync(asset.Id, MediaAssetStatus.PendingUpload, MediaAssetStatus.Uploaded);
        await harness.Repository.TryTransitionAsync(asset.Id, MediaAssetStatus.Uploaded, MediaAssetStatus.Processing);

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage(MediaAssetService.StillProcessingMessage);
        harness.Blobs.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task AttachAsync_ShouldReclaimAndFinish_AnAssetWhoseClaimHasGoneStale()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        var asset = await harness.AssetAsync(upload);
        await harness.Repository.TryTransitionAsync(asset.Id, MediaAssetStatus.PendingUpload, MediaAssetStatus.Uploaded);
        await harness.Repository.TryTransitionAsync(asset.Id, MediaAssetStatus.Uploaded, MediaAssetStatus.Processing);
        harness.Database.Time.Advance(MediaAssetService.StaleProcessingAfter + TimeSpan.FromSeconds(1));

        await harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        (await harness.AssetAsync(upload)).Status.Should().Be(MediaAssetStatus.Ready);
        harness.Blobs.Published.Should().ContainKey(upload.PublicUrl);
    }

    [Fact]
    public async Task AttachAsync_ShouldReleaseTheClaim_WhenPublishingFails_SoARetryCanSucceed()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        harness.Blobs.FailNextPublish = new IOException("storage went away");

        var first = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");
        await first.Should().ThrowAsync<IOException>();

        var afterFailure = await harness.AssetAsync(upload);
        afterFailure.Status.Should().Be(MediaAssetStatus.Uploaded, "a storage fault is not the uploader's fault");
        afterFailure.QuarantineBlobPath.Should().NotBeNull();
        harness.Blobs.Quarantine.Should().ContainKey(afterFailure.QuarantineBlobPath!, "the retry needs the bytes");

        await harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        var asset = await harness.AssetAsync(upload);
        asset.Status.Should().Be(MediaAssetStatus.Ready);
        asset.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task AttachAsync_ShouldNotRecordReady_OrDeleteTheBytes_WhenItsClaimWasTakenOver()
    {
        // This attach is slow; meanwhile another request releases its claim as stale and takes
        // the asset over. Finishing anyway would mark the asset Ready under the new holder and
        // delete the bytes the new holder is about to read.
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        var quarantinePath = (await harness.AssetAsync(upload)).QuarantineBlobPath!;
        harness.Blobs.OnPublish = () => harness.TakeOverClaimAsync(upload);

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<ConflictException>().WithMessage(MediaAssetService.StillProcessingMessage);
        var asset = await harness.AssetAsync(upload);
        asset.Status.Should().Be(MediaAssetStatus.Processing, "the asset is the new holder's to finish");
        asset.AttemptCount.Should().Be(2);
        asset.ValidatedAt.Should().BeNull();
        harness.Blobs.Quarantine.Should().ContainKey(quarantinePath);
    }

    [Fact]
    public async Task AttachAsync_ShouldNotRecordARejection_OrDeleteTheBytes_WhenItsClaimWasTakenOver()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B]);
        var asset = await harness.AssetAsync(upload);
        await harness.Repository.TryTransitionAsync(asset.Id, MediaAssetStatus.PendingUpload, MediaAssetStatus.Uploaded);
        harness.Blobs.OnQuarantineInspect = () => harness.TakeOverClaimAsync(upload);

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<ConflictException>();
        var stored = await harness.AssetAsync(upload);
        stored.Status.Should().Be(MediaAssetStatus.Processing);
        stored.RejectionReason.Should().BeNull();
        harness.Blobs.Quarantine.Should().ContainKey(asset.QuarantineBlobPath!);
    }

    [Fact]
    public async Task AttachAsync_ShouldNotReleaseAClaimItNoLongerHolds_WhenItsPipelineFails()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        harness.Blobs.OnPublish = async () =>
        {
            await harness.TakeOverClaimAsync(upload);
            throw new IOException("storage went away");
        };

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<IOException>();
        (await harness.AssetAsync(upload)).Status.Should().Be(MediaAssetStatus.Processing,
            "the claim belongs to the request that took it over");
    }

    [Fact]
    public async Task AttachAsync_ShouldReleaseTheClaim_WhenNoProcessingSlotIsFree()
    {
        var busy = new Mock<IImageProcessor>();
        busy.Setup(p => p.ProcessAsync(It.IsAny<Stream>(), ImageProcessingProfile.Gallery, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotAvailableException(ImageSharpImageProcessor.BusyMessage));
        await using var harness = await Harness.CreateAsync(busy.Object);
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<NotAvailableException>();
        (await harness.AssetAsync(upload)).Status.Should().Be(MediaAssetStatus.Uploaded);
    }

    [Fact]
    public async Task AttachAsync_ShouldRunTheScopeCheck_BeforeTouchingAnyBytes()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());

        var act = () => harness.Service.AttachAsync(
            harness.UserId,
            upload.PublicUrl,
            "Event images",
            _ => throw new BadRequestException("wrong club"));

        await act.Should().ThrowAsync<BadRequestException>().WithMessage("wrong club");
        harness.Blobs.QuarantineInspections.Should().Be(0);
        (await harness.AssetAsync(upload)).Status.Should().Be(MediaAssetStatus.PendingUpload);
    }

    [Fact]
    public async Task AttachAsync_ShouldRefuseAnUploadIssuedToSomeoneElse()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        var intruder = await harness.Database.SeedUserAsync("intruder");

        var act = () => harness.Service.AttachAsync(intruder.Id, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("*does not belong to this organizer*");
        harness.Blobs.QuarantineInspections.Should().Be(0);
    }

    [Fact]
    public async Task AttachAsync_ShouldRefuse_WhenTheAssetRowDoesNotMatchTheIntent()
    {
        // An intent pointing at another user's asset — only reachable by tampering with the cache
        // — must not let one user attach another's upload.
        await using var harness = await Harness.CreateAsync();
        var theirs = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        var mine = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        harness.WriteIntent(mine.PublicUrl, harness.UserId, theirs.MediaAssetId);

        var act = () => harness.Service.AttachAsync(harness.UserId, mine.PublicUrl, "Event images");

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("*does not belong to this organizer*");
    }

    [Fact]
    public async Task AttachAsync_ShouldRefuse_WhenTheAssetRowIsGone()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        harness.WriteIntent(upload.PublicUrl, harness.UserId, Guid.NewGuid());

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<BadRequestException>().WithMessage("*invalid or expired*");
    }

    [Fact]
    public async Task AttachAsync_ShouldReportAnAssetAwaitingReview_AsAConflict()
    {
        await using var harness = await Harness.CreateAsync();
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        var asset = await harness.AssetAsync(upload);
        await harness.Repository.TryTransitionAsync(asset.Id, MediaAssetStatus.PendingUpload, MediaAssetStatus.Uploaded);
        await harness.Repository.TryTransitionAsync(asset.Id, MediaAssetStatus.Uploaded, MediaAssetStatus.Processing);
        await harness.Repository.TryTransitionAsync(asset.Id, MediaAssetStatus.Processing, MediaAssetStatus.NeedsReview);

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<ConflictException>().WithMessage(MediaAssetService.UnderReviewMessage);
    }

    [Fact]
    public async Task AttachAsync_ShouldFallBackToThePublicContainerChecks_ForAnIntentIssuedWithoutQuarantine()
    {
        // Uploads issued before the flag was switched on are already in the public container.
        await using var harness = await Harness.CreateAsync();
        var url = $"{InMemoryBlobStore.PublicBase}/events/legacy.png";
        harness.Blobs.PublicUploads[url] = InMemoryBlobStore.Image();
        harness.WriteIntent(url, harness.UserId, mediaAssetId: null);

        var intent = await harness.Service.AttachAsync(harness.UserId, url, "Event images");

        intent.MediaAssetId.Should().BeNull();
        harness.Blobs.Published.Should().BeEmpty();
        harness.Blobs.QuarantineInspections.Should().Be(0);
    }

    [Fact]
    public async Task AttachAsync_ShouldApplyTheSizeCap_WithTheCallersSubject()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Blobs.MaxImageBytes = 64;
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Club images");

        await act.Should().ThrowAsync<BadRequestException>().WithMessage("Club images must be smaller than 64 bytes.");
    }

    [Fact]
    public async Task AttachAsync_WithTheWorker_ShouldRequestValidation_AndReturnOnceTheVerdictIsRecorded()
    {
        var publisher = new RecordingPublisher();
        await using var harness = await Harness.CreateAsync(dispatcher: _ => WorkerDispatcher(publisher));
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        var quarantinePath = (await harness.AssetAsync(upload)).QuarantineBlobPath!;
        var reads = harness.SettleOnRead(upload, read: 5);

        await harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Club images");

        var (topic, message) = publisher.Published.Should().ContainSingle().Subject;
        topic.Should().Be("media-validation");
        var request = message.Should().BeOfType<MediaValidationRequestMessage>().Subject;
        request.MediaAssetId.Should().Be(upload.MediaAssetId!.Value);
        request.Attempt.Should().Be(1);
        request.QuarantineBlobPath.Should().Be(quarantinePath);
        request.PublicUrl.Should().Be(upload.PublicUrl);
        request.DeclaredContentType.Should().Be("image/png");
        request.Subject.Should().Be("Club images");

        reads().Should().BeGreaterThanOrEqualTo(5, "the attach waited for the verdict rather than returning early");
        (await harness.AssetAsync(upload)).Status.Should().Be(MediaAssetStatus.Ready);
        harness.Blobs.Published.Should().ContainKey(upload.PublicUrl);
    }

    [Fact]
    public async Task AttachAsync_WithTheWorker_ShouldReportTheWorkersRejection()
    {
        var publisher = new RecordingPublisher();
        await using var harness = await Harness.CreateAsync(dispatcher: _ => WorkerDispatcher(publisher));
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image("image/gif", frames: 2), "image/gif");
        harness.SettleOnRead(upload, read: 4);

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<BadRequestException>().WithMessage(ImageSharpImageProcessor.AnimatedMessage);
        (await harness.AssetAsync(upload)).Status.Should().Be(MediaAssetStatus.Rejected);
    }

    [Fact]
    public async Task AttachAsync_WithTheWorker_ShouldReportStillProcessing_WhenNoVerdictArrivesInTime()
    {
        var publisher = new RecordingPublisher();
        await using var harness = await Harness.CreateAsync(dispatcher: _ => WorkerDispatcher(publisher));
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<ConflictException>().WithMessage(MediaAssetService.StillProcessingMessage);
        var asset = await harness.AssetAsync(upload);
        asset.Status.Should().Be(MediaAssetStatus.Processing,
            "the claim stays with the worker, and the reconciler re-drives it if it never answers");
        asset.AttemptCount.Should().Be(1);
        publisher.Published.Should().ContainSingle();
        harness.Blobs.Quarantine.Should().ContainKey(asset.QuarantineBlobPath!);
    }

    [Fact]
    public async Task AttachAsync_WithTheWorker_ShouldReleaseTheClaim_WhenTheRequestCannotBePublished()
    {
        var publisher = new RecordingPublisher { Failure = new InvalidOperationException("broker down") };
        await using var harness = await Harness.CreateAsync(dispatcher: _ => WorkerDispatcher(publisher));
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());

        var act = () => harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        await act.Should().ThrowAsync<NotAvailableException>()
            .WithMessage(KafkaMediaValidationDispatcher.UnavailableMessage);
        (await harness.AssetAsync(upload)).Status.Should().Be(MediaAssetStatus.Uploaded, "a retry can claim it straight away");
    }

    [Fact]
    public async Task AttachAsync_WithTheWorker_ShouldWaitOnAClaimAnotherAttachHolds_WithoutRequestingItAgain()
    {
        // The user retried the save while the first attach's request was still with the worker.
        var publisher = new RecordingPublisher();
        await using var harness = await Harness.CreateAsync(dispatcher: _ => WorkerDispatcher(publisher));
        var upload = await harness.IssueAndUploadAsync(InMemoryBlobStore.Image());
        var asset = await harness.AssetAsync(upload);
        await harness.Repository.TryTransitionAsync(asset.Id, MediaAssetStatus.PendingUpload, MediaAssetStatus.Uploaded);
        await harness.Repository.TryTransitionAsync(
            asset.Id, MediaAssetStatus.Uploaded, MediaAssetStatus.Processing, new MediaAssetChanges { CountAttempt = true });
        harness.SettleOnRead(upload, read: 3);

        await harness.Service.AttachAsync(harness.UserId, upload.PublicUrl, "Event images");

        publisher.Published.Should().BeEmpty();
        (await harness.AssetAsync(upload)).Status.Should().Be(MediaAssetStatus.Ready);
    }

    [Theory]
    [InlineData(0, 250, 0)]
    [InlineData(10_000, 0, 0)]
    [InlineData(10_000, 250, 40)]
    [InlineData(1_000, 300, 4)]
    public void PollsWithin_ShouldCoverTheWholeWait(int waitMs, int intervalMs, int expected)
    {
        MediaAssetService.PollsWithin(TimeSpan.FromMilliseconds(waitMs), TimeSpan.FromMilliseconds(intervalMs))
            .Should().Be(expected);
    }

    [Fact]
    public void KafkaMediaValidationDispatcher_ShouldWaitLongEnoughForTheWorker_ButWellInsideTheRequestTimeout()
    {
        var dispatcher = new KafkaMediaValidationDispatcher(Mock.Of<IPublisher>());

        dispatcher.SettleWait.Should().Be(TimeSpan.FromSeconds(10));
        dispatcher.PollInterval.Should().Be(TimeSpan.FromMilliseconds(250));
        new InlineMediaValidationDispatcher(null!, null!).SettleWait.Should().Be(TimeSpan.Zero);
    }

    private static KafkaMediaValidationDispatcher WorkerDispatcher(IPublisher publisher) =>
        new(publisher, "media-validation", TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(5));

    [Fact]
    public void BlobUploadIntent_ShouldReadIntentsCachedBeforeMediaAssetIdExisted()
    {
        // Intents live in Redis across a deploy, so the old JSON shape must still deserialize.
        const string cachedByAnOlderBuild =
            "{\"ClubId\":3,\"EventId\":null,\"UserId\":7,\"PublicUrl\":\"https://x/y.png\",\"ContentType\":\"image/png\"}";

        var intent = JsonSerializer.Deserialize<BlobUploadIntent>(cachedByAnOlderBuild);

        intent.Should().Be(new BlobUploadIntent(3, null, 7, "https://x/y.png", "image/png"));
        intent!.MediaAssetId.Should().BeNull();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly Dictionary<string, string> _intents = [];

        public MediaTestDatabase Database { get; private init; } = null!;
        public InMemoryBlobStore Blobs { get; } = new();
        public MediaAssetRepository Repository { get; private init; } = null!;
        public MediaAssetService Service { get; private set; } = null!;
        public MediaValidationPipeline Pipeline { get; private set; } = null!;
        public MediaValidationRecorder Recorder { get; private set; } = null!;
        public int UserId { get; private init; }

        /// <summary>
        /// Runs before the service reads the asset, in line with it. Lets a test record a
        /// worker's verdict mid-wait without touching the context from a second thread.
        /// </summary>
        public Func<Task>? BeforeRead { get; set; }

        public static async Task<Harness> CreateAsync(
            IImageProcessor? processor = null,
            Func<Harness, IMediaValidationDispatcher>? dispatcher = null)
        {
            var database = await MediaTestDatabase.CreateAsync();
            var user = await database.SeedUserAsync();
            var harness = new Harness
            {
                Database = database,
                Repository = database.CreateRepository(),
                UserId = user.Id
            };

            var cache = new Mock<ICacheService>();
            cache.Setup(c => c.GetValueAsync(It.IsAny<string>()))
                .ReturnsAsync((string key) => harness._intents.TryGetValue(key, out var value) ? value : null);

            processor ??= new ImageSharpImageProcessor(Options.Create(new ImageProcessingOptions()));
            harness.Pipeline = new MediaValidationPipeline(harness.Blobs, processor);
            harness.Recorder = new MediaValidationRecorder(harness.Repository, harness.Blobs, database.Time);
            harness.Service = new MediaAssetService(
                database.Db,
                new ReadHookRepository(harness.Repository, () => harness.BeforeRead?.Invoke() ?? Task.CompletedTask),
                harness.Blobs,
                cache.Object,
                dispatcher?.Invoke(harness) ?? new InlineMediaValidationDispatcher(harness.Pipeline, harness.Recorder),
                database.Time);

            return harness;
        }

        /// <summary>Issues an upload the way the presigned endpoint does, intent included.</summary>
        public async Task<PresignedUploadResponse> IssueAsync(string contentType = "image/png")
        {
            var extension = contentType switch
            {
                "image/jpeg" => ".jpg",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                _ => ".png"
            };
            var upload = await Service.IssueUploadAsync(
                new MediaUploadRequest(UserId, null, null, "events/clubs/1/pending", "upload" + extension, contentType));
            WriteIntent(upload.PublicUrl, UserId, upload.MediaAssetId, contentType);
            Database.Db.ChangeTracker.Clear();
            return upload;
        }

        /// <summary>Issues an upload and "PUTs" <paramref name="content"/> to it.</summary>
        public async Task<PresignedUploadResponse> IssueAndUploadAsync(byte[] content, string contentType = "image/png")
        {
            var upload = await IssueAsync(contentType);
            var asset = await AssetAsync(upload);
            Blobs.Put(asset.QuarantineBlobPath!, content, contentType);
            return upload;
        }

        public void WriteIntent(string publicUrl, int userId, Guid? mediaAssetId, string contentType = "image/png") =>
            _intents[BlobUploadIntentValidator.IntentKey(publicUrl)] = JsonSerializer.Serialize(
                new BlobUploadIntent(1, null, userId, publicUrl, contentType, mediaAssetId));

        public async Task<MediaAsset> AssetAsync(PresignedUploadResponse upload) =>
            (await Repository.GetByPublicIdAsync(upload.MediaAssetId!.Value))!;

        /// <summary>
        /// What another request does to a claim it judges stale: release it, then claim it for
        /// itself, which bumps the attempt count.
        /// </summary>
        public async Task TakeOverClaimAsync(PresignedUploadResponse upload)
        {
            var asset = await AssetAsync(upload);
            await Repository.TryTransitionAsync(asset.Id, MediaAssetStatus.Processing, MediaAssetStatus.Uploaded);
            await Repository.TryTransitionAsync(
                asset.Id, MediaAssetStatus.Uploaded, MediaAssetStatus.Processing, new MediaAssetChanges { CountAttempt = true });
        }

        /// <summary>
        /// Plays media-worker and the status consumer: validates the asset's bytes and records
        /// the verdict under <paramref name="attempt"/>, as the API does when the result arrives.
        /// </summary>
        public async Task SettleAsync(PresignedUploadResponse upload, int attempt, string subject = "Event images")
        {
            var asset = await AssetAsync(upload);
            var outcome = await Pipeline.RunAsync(
                asset.QuarantineBlobPath!, asset.PublicUrl!, asset.DeclaredContentType, subject);
            await Recorder.RecordAsync(asset, attempt, outcome);
        }

        /// <summary>
        /// Settles the asset just before the service's <paramref name="read"/>th read of it, and
        /// returns how many reads the service has made.
        /// </summary>
        public Func<int> SettleOnRead(PresignedUploadResponse upload, int read, int attempt = 1)
        {
            var reads = 0;
            BeforeRead = async () =>
            {
                if (++reads == read)
                    await SettleAsync(upload, attempt);
            };
            return () => reads;
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public List<(string Topic, object? Message)> Published { get; } = [];
        public Exception? Failure { get; set; }

        public Task PublishAsync<T>(string topic, T message)
        {
            if (Failure != null)
                throw Failure;

            Published.Add((topic, message));
            return Task.CompletedTask;
        }
    }

    private sealed class ReadHookRepository(IMediaAssetRepository inner, Func<Task> beforeRead) : IMediaAssetRepository
    {
        public Task AddAsync(MediaAsset asset) => inner.AddAsync(asset);

        public async Task<MediaAsset?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default)
        {
            await beforeRead();
            return await inner.GetByPublicIdAsync(publicId, cancellationToken);
        }

        public Task<MediaAsset?> GetByQuarantineBlobPathAsync(string quarantineBlobPath, CancellationToken cancellationToken = default) =>
            inner.GetByQuarantineBlobPathAsync(quarantineBlobPath, cancellationToken);

        public Task<bool> TryTransitionAsync(
            int id,
            MediaAssetStatus from,
            MediaAssetStatus to,
            MediaAssetChanges? changes = null,
            CancellationToken cancellationToken = default,
            int? whenAttempt = null) =>
            inner.TryTransitionAsync(id, from, to, changes, cancellationToken, whenAttempt);

        public Task<List<MediaAsset>> GetUnattachedIssuedBeforeAsync(DateTime createdBefore, int limit, CancellationToken cancellationToken = default) =>
            inner.GetUnattachedIssuedBeforeAsync(createdBefore, limit, cancellationToken);

        public Task<List<MediaAsset>> GetProcessingClaimedBeforeAsync(DateTime updatedBefore, int limit, CancellationToken cancellationToken = default) =>
            inner.GetProcessingClaimedBeforeAsync(updatedBefore, limit, cancellationToken);

        public Task<List<MediaAsset>> GetStalledBeforeAsync(DateTime updatedBefore, int limit, CancellationToken cancellationToken = default) =>
            inner.GetStalledBeforeAsync(updatedBefore, limit, cancellationToken);
    }
}
