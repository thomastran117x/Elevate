using backend.main.shared.storage;

using Moq;

namespace backend.tests.Integration.Infrastructure;

/// <summary>
/// Attach paths inspect what actually landed in storage, but the suites that drive a service
/// directly over a <see cref="Mock{T}"/> never upload anything. These stand in for a blob that
/// passes every check, so those tests keep exercising what they were written for.
/// </summary>
/// <remarks>
/// Tests that go through <see cref="AuthApiTestApp"/> use <see cref="FakeAzureBlobService"/>
/// instead, which stages a valid image whenever it mints a URL.
/// </remarks>
public static class BlobInspectionStubs
{
    public const long DefaultMaxImageBytes = 5L * 1024 * 1024;

    /// <summary>
    /// Makes every inspected URL report a small, valid PNG under the default cap, and accepts the
    /// content-type restamp that follows a successful inspection.
    /// </summary>
    public static Mock<IAzureBlobService> StubAcceptableBlobs(this Mock<IAzureBlobService> blobService)
    {
        blobService
            .Setup(service => service.MaxImageBytes)
            .Returns(DefaultMaxImageBytes);
        blobService
            .Setup(service => service.InspectBlobAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlobInspection(1024, "image/png", FakeAzureBlobService.HeaderFor("image/png")));
        blobService
            .Setup(service => service.NormalizeBlobHeadersAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return blobService;
    }
}
