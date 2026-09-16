namespace backend.main.features.events.contracts.responses
{
    /// <summary>
    /// One image in an event's gallery, with the metadata a client needs to render it accessibly
    /// and in the organizer's chosen order.
    /// </summary>
    public sealed class EventImageResponse
    {
        public int Id
        {
            get; set;
        }
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Null when nobody has written alt text yet. Distinct from a decorative image, which is
        /// deliberately unlabelled — see <see cref="IsDecorative"/>.
        /// </summary>
        public string? AltText
        {
            get; set;
        }

        public bool IsDecorative
        {
            get; set;
        }
        public bool IsCover
        {
            get; set;
        }
        public int SortOrder
        {
            get; set;
        }

        /// <summary>True when the image still needs alt text, so the editor can flag it.</summary>
        public bool NeedsAltText
        {
            get; set;
        }

        public DateTime CreatedAt
        {
            get; set;
        }
        public DateTime UpdatedAt
        {
            get; set;
        }
    }
}
