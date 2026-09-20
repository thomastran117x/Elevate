using backend.main.features.cache;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage;

namespace backend.main.features.events.images;

/// <summary>
/// The event-scoped half of upload-intent validation: the shared validator proves the URL was
/// issued to this user, and this adds the club/event checks specific to an event gallery.
/// <para>
/// Kept separate from <see cref="BlobUploadIntentValidator"/> so the recurrence series feature
/// enforces exactly the same checks. Without it a club manager could attach another organizer's
/// blob URL to every future occurrence in one request.
/// </para>
/// </summary>
internal static class EventImageUploadValidator
{
    internal static TimeSpan IntentTtl => BlobUploadIntentValidator.IntentTtl;

    internal static string IntentKey(string imageUrl) =>
        BlobUploadIntentValidator.IntentKey(imageUrl);

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
        var intent = await BlobUploadIntentValidator.RequireIntentAsync(
            blobService, cache, userId, imageUrl, "Event images");

        if (intent.ClubId != clubId)
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
