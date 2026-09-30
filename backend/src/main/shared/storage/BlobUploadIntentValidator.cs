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
/// their TTL, so property names must not change and new properties may only be appended with a
/// default. <c>MediaAssetId</c> is such an addition — an intent cached before it existed, or
/// issued while <c>storage.quarantine</c> is off, deserializes with it null.
/// </remarks>
public sealed record BlobUploadIntent(
    int ClubId,
    int? EventId,
    int UserId,
    string PublicUrl,
    string ContentType,
    Guid? MediaAssetId = null
);

/// <summary>
/// Proves that an image URL came from a presigned upload this service issued, to this user —
/// rather than being any URL a caller pasted in — and, for uploads that went straight to the
/// public container, that what was actually uploaded is an image within the configured size cap.
/// <para>
/// Without the intent check, an owned-container URL belonging to another user is
/// indistinguishable from one's own: <c>IsOwnedBlobUrl</c> only proves the blob lives in our
/// container, not who uploaded it.
/// </para>
/// <para>
/// Static, and shared by both implementations of <c>IMediaAssetService</c>: the pass-through one
/// used while <c>storage.quarantine</c> is off calls <see cref="RequireIntentAsync"/> exactly as
/// the attach paths used to, and the quarantine one resolves the intent here and then validates
/// the quarantined bytes itself. The attach paths reach both through the injected service, which
/// is what lets the flag choose between them.
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
        var intent = await ResolveIntentAsync(blobService, cache, userId, imageUrl, subject);

        await RequireAcceptableBlobAsync(blobService, imageUrl, intent, subject);

        return intent;
    }

    /// <summary>
    /// The ownership half of <see cref="RequireIntentAsync"/>: the URL is ours and a live intent
    /// issued to <paramref name="userId"/> stands behind it. Touches no blob.
    /// </summary>
    internal static async Task<BlobUploadIntent> ResolveIntentAsync(
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

        return intent;
    }

    /// <summary>
    /// Checks what actually landed in the public container. A SAS bounds neither the size nor the
    /// content of an upload, and without quarantine the container is publicly readable, so attach
    /// time is the only moment where the bytes exist and we still hold a handle to them.
    /// </summary>
    /// <remarks>
    /// A rejected blob is deleted here rather than left to <c>OrphanBlobCleanupRunner</c>, which
    /// is feature-gated off by default and never touches anything younger than its
    /// <c>MinAgeHours</c> floor. Deletion is best-effort and swallows its own failures, so it
    /// cannot mask the rejection.
    /// </remarks>
    internal static async Task RequireAcceptableBlobAsync(
        IAzureBlobService blobService,
        string imageUrl,
        BlobUploadIntent intent,
        string subject)
    {
        var inspection = await blobService.InspectBlobAsync(imageUrl);
        if (inspection == null)
            throw new BadRequestException(ImageUploadGate.DidNotCompleteMessage);

        var rejection = ImageUploadGate.Evaluate(
            inspection.Value, intent.ContentType, blobService.MaxImageBytes, subject, out var signature);

        if (rejection != null)
        {
            await blobService.DeleteBlobAsync(imageUrl);
            throw new BadRequestException(rejection);
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
}
