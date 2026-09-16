namespace backend.main.features.events.images;

public class EventImage
{
    public int Id
    {
        get; set;
    }
    public int EventId
    {
        get; set;
    }
    public required string ImageUrl
    {
        get; set;
    }
    public int SortOrder { get; set; } = 0;

    /// <summary>
    /// The image shown wherever an event is represented by a single picture. At most one per
    /// event, enforced by a partial unique index rather than by application code alone.
    /// </summary>
    public bool IsCover { get; set; } = false;

    /// <summary>
    /// Alternative text for screen readers. Null on rows predating gallery management; new and
    /// edited images must supply this or set <see cref="IsDecorative"/>.
    /// </summary>
    public string? AltText
    {
        get; set;
    }

    /// <summary>
    /// Marks an image as conveying nothing beyond decoration, which renders it with an empty alt
    /// attribute. An explicit flag rather than an empty string, so "deliberately decorative" is
    /// distinguishable from "nobody has written alt text yet".
    /// </summary>
    public bool IsDecorative { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// True when the image needs alt text a person has not written yet. Not mapped — derived for
    /// the manage view so the editor can surface the gap on rows that predate the requirement.
    /// </summary>
    public bool NeedsAltText => !IsDecorative && string.IsNullOrWhiteSpace(AltText);

    // Navigation
    public Events Event { get; set; } = null!;
}
