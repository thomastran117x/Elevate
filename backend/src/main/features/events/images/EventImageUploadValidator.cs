using backend.main.features.media;
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
    /// Validates every URL that is not already attached to the event, as one unit: each image is
    /// handed off before the save waits, once, for all of them.
    /// </summary>
    /// <param name="existingUrls">
    /// URLs the event already holds. These skip validation because their upload intent has long
    /// since expired, and re-submitting an image the event already has is not a new upload.
    /// </param>
    internal static async Task ValidateAsync(
        IMediaAssetService mediaAssets,
        int clubId,
        int userId,
        IEnumerable<string> imageUrls,
        int? eventId = null,
        ISet<string>? existingUrls = null,
        CancellationToken cancellationToken = default)
    {
        var attachments = imageUrls
            .Where(imageUrl => existingUrls?.Contains(imageUrl) != true)
            .Distinct(StringComparer.Ordinal)
            .Select(imageUrl => new MediaAttachment(
                imageUrl,
                "Event images",
                intent => RequireEventScope(intent, clubId, eventId)))
            .ToList();

        if (attachments.Count > 0)
            await mediaAssets.AttachAllAsync(userId, attachments, cancellationToken);
    }

    private static void RequireEventScope(BlobUploadIntent intent, int clubId, int? eventId)
    {
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
