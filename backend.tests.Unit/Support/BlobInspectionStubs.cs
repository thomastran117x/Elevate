using backend.main.shared.storage;

using Moq;

namespace backend.tests.Unit.Support;

/// <summary>
/// Attach paths inspect what actually landed in storage, but uploads in unit tests are notional —
/// no bytes are ever PUT to a presigned URL. These stand in for a blob that passes every check,
/// and let a test that cares stage one that should not.
/// </summary>
public static class BlobInspectionStubs
{
    public const long DefaultMaxImageBytes = 5L * 1024 * 1024;

    /// <summary>
    /// The leading bytes of a real file of the given type, long enough for every signature the
    /// inspector knows about.
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
    public static BlobInspection Image(string contentType = "image/png", long contentLength = 1024) =>
        new(contentLength, contentType, HeaderFor(contentType));

    /// <summary>
    /// Makes every inspected URL report a small, valid PNG under the default cap.
    /// </summary>
    public static Mock<IAzureBlobService> StubAcceptableBlobs(this Mock<IAzureBlobService> blobService) =>
        blobService.StubBlobInspection(Image());

    /// <summary>
    /// Makes every inspected URL report <paramref name="inspection"/>, or, when it is null, a blob
    /// that is not there at all.
    /// </summary>
    public static Mock<IAzureBlobService> StubBlobInspection(
        this Mock<IAzureBlobService> blobService,
        BlobInspection? inspection)
    {
        blobService
            .Setup(service => service.MaxImageBytes)
            .Returns(DefaultMaxImageBytes);
        blobService
            .Setup(service => service.InspectBlobAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(inspection);

        return blobService;
    }
}
