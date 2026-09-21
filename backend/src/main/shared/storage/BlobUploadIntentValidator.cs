using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using backend.main.features.cache;
using backend.main.shared.exceptions.http;

namespace backend.main.shared.storage;

/// <summary>
/// The intent recorded when a presigned upload URL is issued, so the URL can later be proved to
/// belong to the user and club that asked for it.
/// </summary>
/// <remarks>
/// <c>ClubId</c> is 0 for a club-creation upload, which is issued before the club exists. The
/// JSON shape is load-bearing: intents written by earlier builds are still live in the cache for
/// their TTL, so property names must not change.
/// </remarks>
internal sealed record BlobUploadIntent(
    int ClubId,
    int? EventId,
    int UserId,
    string PublicUrl,
    string ContentType
);

/// <summary>
/// Proves that an image URL came from a presigned upload this service issued, to this user —
/// rather than being any URL a caller pasted in — and that what was actually uploaded is an image
/// within the configured size cap.
/// <para>
/// Shared by the event and club attach paths so both enforce the same checks. Without the intent
/// check, an owned-container URL belonging to another user is indistinguishable from one's own:
/// <c>IsOwnedBlobUrl</c> only proves the blob lives in our container, not who uploaded it.
/// </para>
/// <para>
/// Static rather than injected because <c>EventsServiceHarness</c> constructs <c>EventsService</c>
/// positionally, so its constructor must not gain dependencies. The size cap therefore travels on
/// <see cref="IAzureBlobService.MaxImageBytes"/>, which every caller already passes in.
/// </para>
/// </summary>
internal static class BlobUploadIntentValidator
{
    internal static readonly TimeSpan IntentTtl = TimeSpan.FromMinutes(20);

    /// <remarks>
    /// The <c>event:</c> prefix is historical — club uploads are issued by the same endpoint and
    /// have always been stored under it. Renaming it would strand every in-flight intent.
    /// </remarks>
    internal static string IntentKey(string imageUrl)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl));

        return $"event:image-upload:intent:{Convert.ToHexString(bytes)}";
    }

    /// <summary>
    /// Resolves the upload intent behind a URL, rejecting anything that is not an HTTPS URL in our
    /// own container backed by a live intent issued to <paramref name="userId"/>, and anything
    /// whose stored bytes are not an acceptable image.
    /// </summary>
    /// <param name="subject">
    /// Names the thing being attached in the error message — "Event images" or "Club images".
    /// </param>
    internal static async Task<BlobUploadIntent> RequireIntentAsync(
        IAzureBlobService blobService,
        ICacheService cache,
        int userId,
        string imageUrl,
        string subject)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException($"{subject} must use a valid HTTPS URL.");
        }

        if (!blobService.IsOwnedBlobUrl(imageUrl))
        {
            throw new BadRequestException(
                $"{subject} must reference uploads issued by this service.");
        }

        var intentPayload = await cache.GetValueAsync(IntentKey(imageUrl));
        if (intentPayload == null)
        {
            throw new BadRequestException(
                "Image upload is invalid or expired. Please upload the image again.");
        }

        var intent = JsonSerializer.Deserialize<BlobUploadIntent>(intentPayload);
        if (intent == null ||
            intent.UserId != userId ||
            !string.Equals(intent.PublicUrl, imageUrl, StringComparison.Ordinal))
        {
            throw new BadRequestException(
                "Image upload is invalid or does not belong to this organizer.");
        }

        await RequireAcceptableBlobAsync(blobService, imageUrl, intent, subject);

        return intent;
    }

    /// <summary>
    /// Checks what actually landed in storage. A SAS bounds neither the size nor the content of
    /// an upload, and the container is publicly readable, so attach time is the only moment where
    /// the bytes exist and we still hold a handle to them.
    /// </summary>
    /// <remarks>
    /// A rejected blob is deleted here rather than left to <c>OrphanBlobCleanupRunner</c>, which
    /// is feature-gated off by default and never touches anything younger than its
    /// <c>MinAgeHours</c> floor. Deletion is best-effort and swallows its own failures, so it
    /// cannot mask the rejection.
    /// </remarks>
    private static async Task RequireAcceptableBlobAsync(
        IAzureBlobService blobService,
        string imageUrl,
        BlobUploadIntent intent,
        string subject)
    {
        var inspection = await blobService.InspectBlobAsync(imageUrl);
        if (inspection == null)
        {
            throw new BadRequestException(
                "Image upload did not complete. Please upload the image again.");
        }

        var blob = inspection.Value;

        if (blob.ContentLength <= 0)
        {
            await blobService.DeleteBlobAsync(imageUrl);

            throw new BadRequestException(
                "Image upload did not complete. Please upload the image again.");
        }

        if (blob.ContentLength > blobService.MaxImageBytes)
        {
            await blobService.DeleteBlobAsync(imageUrl);

            throw new BadRequestException(
                $"{subject} must be smaller than {DescribeLimit(blobService.MaxImageBytes)}.");
        }

        if (!ImageSignatureInspector.TryDetect(blob.HeaderBytes, out var signature))
        {
            await blobService.DeleteBlobAsync(imageUrl);

            throw new BadRequestException(
                "Only JPEG, PNG, WEBP, and GIF images are supported.");
        }

        // The type the uploader asked for is cross-checked against the bytes. It is skipped when
        // unrecognised, because the browser sends "application/octet-stream" for a file whose
        // type it cannot determine, and the presigned endpoint derived the stored type from the
        // file extension in that case.
        if (TryResolveDeclaredFormat(intent.ContentType, out var declaredFormat) &&
            signature.Format != declaredFormat)
        {
            await blobService.DeleteBlobAsync(imageUrl);

            throw new BadRequestException(
                "The uploaded file does not match the image type that was selected.");
        }

        // Every header on the blob is whatever the client put on its own PUT — the SAS content
        // type only overrides reads made through that SAS, not the anonymous public URL. So the
        // header set is rewritten from the bytes rather than checked: matching bytes against a
        // caller-chosen label still leaves the label deciding what the world is served, and a
        // real GIF labelled text/html would pass every check above and then be executed.
        //
        // Unconditional, because the content type is not the only header that matters. A blob
        // whose type is already right can still carry the uploader's Content-Disposition
        // ("attachment; filename=invoice.exe"), Cache-Control or Content-Encoding, and skipping
        // the rewrite when the type matches would leave exactly those in place.
        await blobService.NormalizeBlobHeadersAsync(imageUrl, signature.ContentType);
    }

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
            case "image/webp":
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
