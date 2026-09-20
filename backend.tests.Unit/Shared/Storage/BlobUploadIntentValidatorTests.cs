using System.Text.Json;

using backend.main.features.cache;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage;

using backend.tests.Unit.Support;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Shared.Storage;

/// <summary>
/// The attach-time half of the presigned upload flow. A SAS bounds neither the size nor the
/// content of an upload — the browser PUTs straight to Azure — so these pin the checks that run
/// once the bytes are in storage and we still hold a handle to them.
/// </summary>
public class BlobUploadIntentValidatorTests
{
    private const string ImageUrl = "https://cdn.test/events/poster.png";
    private const int UserId = 7;
    private const int ClubId = 4;

    [Fact]
    public async Task RequireIntentAsync_ShouldReturnTheIntent_ForAnImageWithinTheCap()
    {
        var harness = new Harness(BlobInspectionStubs.Image());

        var intent = await harness.ValidateAsync();

        intent.ClubId.Should().Be(ClubId);
        intent.UserId.Should().Be(UserId);
        harness.BlobService.Verify(service => service.DeleteBlobAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RequireIntentAsync_ShouldReject_WhenTheBlobWasNeverUploaded()
    {
        // Nothing landed, so there is nothing to delete either.
        var harness = new Harness(inspection: null);

        await harness.Invoking(h => h.ValidateAsync())
            .Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("Image upload did not complete*");

        harness.BlobService.Verify(service => service.DeleteBlobAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RequireIntentAsync_ShouldRejectAndDelete_WhenTheBlobIsEmpty()
    {
        var harness = new Harness(new BlobInspection(0, "image/png", []));

        await harness.Invoking(h => h.ValidateAsync())
            .Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("Image upload did not complete*");

        harness.VerifyDeletedOnce();
    }

    [Fact]
    public async Task RequireIntentAsync_ShouldRejectAndDelete_WhenTheBlobExceedsTheCap()
    {
        var harness = new Harness(BlobInspectionStubs.Image(contentLength: BlobInspectionStubs.DefaultMaxImageBytes + 1));

        await harness.Invoking(h => h.ValidateAsync())
            .Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("Event images must be smaller than 5MB.");

        harness.VerifyDeletedOnce();
    }

    [Fact]
    public async Task RequireIntentAsync_ShouldReportTheConfiguredCap_NotAHardcodedOne()
    {
        var harness = new Harness(
            BlobInspectionStubs.Image(contentLength: 3 * 1024 * 1024),
            maxBytes: 2L * 1024 * 1024);

        await harness.Invoking(h => h.ValidateAsync())
            .Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("Event images must be smaller than 2MB.");
    }

    [Fact]
    public async Task RequireIntentAsync_ShouldRejectAndDelete_WhenTheBytesAreNotAnImage()
    {
        // A SAS is valid for any bytes at all, so the only thing that says this is an image is
        // the image itself.
        var harness = new Harness(new BlobInspection(2048, "image/png", [0x4D, 0x5A, 0x90, 0x00]));

        await harness.Invoking(h => h.ValidateAsync())
            .Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("Only JPEG, PNG, WEBP, and GIF images are supported.");

        harness.VerifyDeletedOnce();
    }

    [Fact]
    public async Task RequireIntentAsync_ShouldRejectAndDelete_WhenTheBytesDisagreeWithTheDeclaredType()
    {
        // The container is publicly readable and Azure serves the blob as its stored content
        // type, so bytes that are not what the type claims must never be attached.
        var harness = new Harness(
            new BlobInspection(2048, "image/png", BlobInspectionStubs.HeaderFor("image/gif")),
            intentContentType: "image/png");

        await harness.Invoking(h => h.ValidateAsync())
            .Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("The uploaded file does not match the image type that was selected.");

        harness.VerifyDeletedOnce();
    }

    [Fact]
    public async Task RequireIntentAsync_ShouldAcceptJpegBytes_WhenTheIntentDeclaredImageJpg()
    {
        // The presigned endpoint accepts "image/jpg" as well as "image/jpeg"; the inspector only
        // ever reports the canonical one, so these have to compare as the same format.
        var harness = new Harness(
            new BlobInspection(2048, "image/jpg", BlobInspectionStubs.HeaderFor("image/jpeg")),
            intentContentType: "image/jpg");

        var intent = await harness.ValidateAsync();

        intent.ContentType.Should().Be("image/jpg");
        harness.BlobService.Verify(service => service.DeleteBlobAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RequireIntentAsync_ShouldFallBackToTheStoredType_WhenTheBrowserCouldNotDetermineOne()
    {
        // The frontend sends "application/octet-stream" when the file has no type of its own, and
        // the presigned endpoint then derives the stored type from the file extension.
        var harness = new Harness(
            new BlobInspection(2048, "image/png", BlobInspectionStubs.HeaderFor("image/gif")),
            intentContentType: "application/octet-stream");

        await harness.Invoking(h => h.ValidateAsync())
            .Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("The uploaded file does not match the image type that was selected.");

        harness.VerifyDeletedOnce();
    }

    [Fact]
    public async Task RequireIntentAsync_ShouldAcceptAnySupportedImage_WhenNeitherTypeIsRecognised()
    {
        var harness = new Harness(
            new BlobInspection(2048, "application/octet-stream", BlobInspectionStubs.HeaderFor("image/webp")),
            intentContentType: "application/octet-stream");

        var intent = await harness.ValidateAsync();

        intent.PublicUrl.Should().Be(ImageUrl);
        harness.BlobService.Verify(service => service.DeleteBlobAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RequireIntentAsync_ShouldNotInspectStorage_WhenTheUrlFailsAnEarlierCheck()
    {
        // The cheap checks run first, so a pasted or expired URL never costs a storage round trip.
        var harness = new Harness(BlobInspectionStubs.Image(), hasIntent: false);

        await harness.Invoking(h => h.ValidateAsync())
            .Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("Image upload is invalid or expired*");

        harness.BlobService.Verify(
            service => service.InspectBlobAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class Harness
    {
        private readonly string? _intentPayload;

        public Harness(
            BlobInspection? inspection,
            string intentContentType = "image/png",
            long maxBytes = BlobInspectionStubs.DefaultMaxImageBytes,
            bool hasIntent = true)
        {
            _intentPayload = hasIntent
                ? JsonSerializer.Serialize(
                    new BlobUploadIntent(ClubId, null, UserId, ImageUrl, intentContentType))
                : null;

            BlobService.Setup(service => service.IsOwnedBlobUrl(It.IsAny<string>())).Returns(true);
            BlobService.Setup(service => service.MaxImageBytes).Returns(maxBytes);
            BlobService
                .Setup(service => service.InspectBlobAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(inspection);
            BlobService
                .Setup(service => service.DeleteBlobAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            Cache.Setup(cache => cache.GetValueAsync(It.IsAny<string>())).ReturnsAsync(() => _intentPayload);
        }

        public Mock<IAzureBlobService> BlobService { get; } = new();

        public Mock<ICacheService> Cache { get; } = new();

        public Task<BlobUploadIntent> ValidateAsync() =>
            BlobUploadIntentValidator.RequireIntentAsync(
                BlobService.Object, Cache.Object, UserId, ImageUrl, "Event images");

        public void VerifyDeletedOnce() =>
            BlobService.Verify(service => service.DeleteBlobAsync(ImageUrl), Times.Once);
    }
}
