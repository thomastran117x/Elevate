using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using backend.main.features.cache;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage;

namespace backend.main.features.events.images;

/// <summary>
/// The intent recorded when a presigned upload URL is issued, so the URL can later be proved to
/// belong to the organizer and club that asked for it.
/// </summary>
internal sealed record EventImageUploadIntent(
    int ClubId,
    int? EventId,
    int UserId,
    string PublicUrl,
    string ContentType
);

/// <summary>
/// Proves that an image URL came from a presigned upload this service issued, to this user, for
/// this club — rather than being any URL a caller pasted in.
/// <para>
/// Extracted from <c>EventsService</c> so the recurrence series feature enforces exactly the same
/// checks. Without it a club manager could attach another organizer's blob URL to every future
/// occurrence in one request. Static rather than injected for the same reason the version
/// recorder is: <c>EventsServiceHarness</c> constructs <c>EventsService</c> positionally, so its
/// constructor must not gain dependencies.
/// </para>
/// </summary>
internal static class EventImageUploadValidator
{
    internal static readonly TimeSpan IntentTtl = TimeSpan.FromMinutes(20);

    internal static string IntentKey(string imageUrl)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl));

        return $"event:image-upload:intent:{Convert.ToHexString(bytes)}";
    }

    /// <summary>
    /// Validates every URL that is not already attached to the event.
    /// </summary>
    /// <param name="existingUrls">
    /// URLs the event already holds. These skip validation because their upload intent has long
    /// since expired, and re-submitting an image the event already has is not a new upload.
    /// </param>
    internal static async Task ValidateAsync(
        IAzureBlobService blobService,
        ICacheService cache,
        int clubId,
        int userId,
        IEnumerable<string> imageUrls,
        int? eventId = null,
        ISet<string>? existingUrls = null)
    {
        foreach (var imageUrl in imageUrls)
        {
            if (existingUrls?.Contains(imageUrl) == true)
                continue;

            await ValidateOneAsync(blobService, cache, clubId, userId, imageUrl, eventId);
        }
    }

    private static async Task ValidateOneAsync(
        IAzureBlobService blobService,
        ICacheService cache,
        int clubId,
        int userId,
        string imageUrl,
        int? eventId)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("Event images must use a valid HTTPS URL.");
        }

        if (!blobService.IsOwnedBlobUrl(imageUrl))
        {
            throw new BadRequestException(
                "Event images must reference uploads issued by this service.");
        }

        var intentPayload = await cache.GetValueAsync(IntentKey(imageUrl));
        if (intentPayload == null)
        {
            throw new BadRequestException(
                "Image upload is invalid or expired. Please upload the image again.");
        }

        var intent = JsonSerializer.Deserialize<EventImageUploadIntent>(intentPayload);
        if (intent == null ||
            intent.UserId != userId ||
            intent.ClubId != clubId ||
            !string.Equals(intent.PublicUrl, imageUrl, StringComparison.Ordinal))
        {
            throw new BadRequestException(
                "Image upload is invalid or does not belong to this organizer.");
        }

        if (intent.EventId.HasValue && intent.EventId != eventId)
        {
            throw new BadRequestException(
                "Image upload does not belong to the specified event.");
        }
    }
}
