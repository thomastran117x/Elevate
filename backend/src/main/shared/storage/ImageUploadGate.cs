using System.Globalization;

namespace backend.main.shared.storage;

/// <summary>
/// The checks every uploaded image must pass before anything else is done with it: something
/// arrived, it is within the size cap, its leading bytes are one of the four supported formats,
/// and that format is the one the uploader declared.
/// </summary>
/// <remarks>
/// Pure, so the public-container gate that runs while <c>storage.quarantine</c> is off and the
/// quarantine pipeline that runs while it is on reject the same files with the same words.
/// </remarks>
internal static class ImageUploadGate
{
    internal const string DidNotCompleteMessage = "Image upload did not complete. Please upload the image again.";
    internal const string UnsupportedFormatMessage = "Only JPEG, PNG, WEBP, and GIF images are supported.";
    internal const string MismatchMessage = "The uploaded file does not match the image type that was selected.";

    /// <summary>
    /// Returns why <paramref name="blob"/> is unacceptable, or null when it passes.
    /// </summary>
    /// <param name="subject">
    /// Names the thing being uploaded in the size message — "Event images" or "Club images".
    /// </param>
    /// <param name="signature">The detected format, when the blob passes.</param>
    internal static string? Evaluate(
        BlobInspection blob,
        string? declaredContentType,
        long maxBytes,
        string subject,
        out ImageSignature signature)
    {
        signature = default;

        if (blob.ContentLength <= 0)
            return DidNotCompleteMessage;

        if (blob.ContentLength > maxBytes)
            return TooLargeMessage(subject, maxBytes);

        if (!ImageSignatureInspector.TryDetect(blob.HeaderBytes, out signature))
            return UnsupportedFormatMessage;

        // The type the uploader asked for is cross-checked against the bytes. It is skipped when
        // unrecognised, because the browser sends "application/octet-stream" for a file whose
        // type it cannot determine, and the presigned endpoint derived the stored type from the
        // file extension in that case.
        if (TryResolveDeclaredFormat(declaredContentType, out var declaredFormat) &&
            signature.Format != declaredFormat)
        {
            return MismatchMessage;
        }

        return null;
    }

    internal static string TooLargeMessage(string subject, long maxBytes) =>
        $"{subject} must be smaller than {DescribeLimit(maxBytes)}.";

    /// <remarks>
    /// Matched by format rather than by string: the presigned endpoint accepts <c>image/jpg</c>
    /// as well as <c>image/jpeg</c>, while the inspector only ever reports the canonical
    /// <c>image/jpeg</c>.
    /// </remarks>
    private static bool TryResolveDeclaredFormat(string? contentType, out ImageFormat format)
    {
        switch ((contentType ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "image/jpeg":
            case "image/jpg":
                format = ImageFormat.Jpeg;
                return true;
            case "image/png":
                format = ImageFormat.Png;
                return true;
            case WebpMedia.ContentType:
                format = ImageFormat.Webp;
                return true;
            case "image/gif":
                format = ImageFormat.Gif;
                return true;
            default:
                format = default;
                return false;
        }
    }

    private static string DescribeLimit(long maxBytes)
    {
        var megabytes = maxBytes / (double)(1024 * 1024);

        return megabytes >= 1
            ? $"{megabytes.ToString("0.#", CultureInfo.InvariantCulture)}MB"
            : $"{Math.Max(maxBytes, 0).ToString(CultureInfo.InvariantCulture)} bytes";
    }
}
