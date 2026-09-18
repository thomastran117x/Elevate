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
/// rather than being any URL a caller pasted in.
/// <para>
/// Shared by the event and club attach paths so both enforce the same checks. Without the intent
/// check, an owned-container URL belonging to another user is indistinguishable from one's own:
/// <c>IsOwnedBlobUrl</c> only proves the blob lives in our container, not who uploaded it.
/// </para>
/// <para>
/// Static rather than injected because <c>EventsServiceHarness</c> constructs <c>EventsService</c>
/// positionally, so its constructor must not gain dependencies.
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
    /// own container backed by a live intent issued to <paramref name="userId"/>.
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

        return intent;
    }
}
