using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.contracts.requests
{
    /// <summary>
    /// Alt text and the decorative flag are mutually exclusive: a decorative image renders with an
    /// empty alt attribute, so any text on it would silently never be read.
    /// </summary>
    /// <remarks>
    /// Note what is deliberately NOT required on attach — that every image have one or the other.
    /// An image is uploaded before anyone has written a description for it, and rows predating
    /// gallery management have neither, so demanding it here would either block the upload or
    /// push organizers to mark real content decorative to get past it. The requirement applies
    /// when metadata is actually written (<see cref="UpdateEventImageRequest"/>): you cannot
    /// clear a description into limbo. Images still awaiting one are reported by
    /// <c>EventImage.NeedsAltText</c> so the editor can surface them.
    /// </remarks>
    internal static class EventImageAltTextRule
    {
        internal const int MaxAltTextLength = 300;

        /// <param name="requireDescription">
        /// True when the caller is writing metadata rather than attaching a file, in which case
        /// leaving an image with neither a description nor the decorative flag is not a state to
        /// save — it is the state the edit was opened to resolve.
        /// </param>
        internal static ValidationResult? Validate(
            string? altText,
            bool isDecorative,
            bool requireDescription = false)
        {
            if (isDecorative)
            {
                if (!string.IsNullOrWhiteSpace(altText))
                {
                    return new ValidationResult(
                        "A decorative image cannot also have alt text.",
                        new[] { nameof(AddEventImageRequest.AltText) });
                }

                return null;
            }

            if (requireDescription && string.IsNullOrWhiteSpace(altText))
            {
                return new ValidationResult(
                    "Provide alt text describing the image, or mark it as decorative.",
                    new[] { nameof(AddEventImageRequest.AltText) });
            }

            return null;
        }
    }

    public class AddEventImageRequest : IValidatableObject
    {
        [Required]
        [StringLength(2048)]
        public string ImageUrl { get; set; } = null!;

        [StringLength(EventImageAltTextRule.MaxAltTextLength)]
        public string? AltText
        {
            get; set;
        }

        public bool IsDecorative
        {
            get; set;
        }

        /// <summary>Makes the new image the event's cover once it is attached.</summary>
        public bool IsCover
        {
            get; set;
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!Uri.TryCreate(ImageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                yield return new ValidationResult(
                    "ImageUrl must be a valid HTTPS URL.",
                    new[] { nameof(ImageUrl) }
                );
            }

            var altTextResult = EventImageAltTextRule.Validate(AltText, IsDecorative);
            if (altTextResult != null)
                yield return altTextResult;
        }
    }

    /// <summary>Edits the accessibility metadata of an image already attached to the event.</summary>
    public class UpdateEventImageRequest : IValidatableObject
    {
        [StringLength(EventImageAltTextRule.MaxAltTextLength)]
        public string? AltText
        {
            get; set;
        }

        public bool IsDecorative
        {
            get; set;
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var altTextResult = EventImageAltTextRule.Validate(
                AltText, IsDecorative, requireDescription: true);
            if (altTextResult != null)
                yield return altTextResult;
        }
    }

    /// <summary>Swaps the file behind an image slot, keeping its order, cover flag and alt text.</summary>
    public class ReplaceEventImageRequest : IValidatableObject
    {
        [Required]
        [StringLength(2048)]
        public string ImageUrl { get; set; } = null!;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!Uri.TryCreate(ImageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                yield return new ValidationResult(
                    "ImageUrl must be a valid HTTPS URL.",
                    new[] { nameof(ImageUrl) }
                );
            }
        }
    }

    /// <summary>
    /// The event's images in their new order. Must list every image exactly once — a partial list
    /// is rejected rather than applied, so a client working from a stale gallery cannot silently
    /// drop an image from the ordering.
    /// </summary>
    public class ReorderEventImagesRequest : IValidatableObject
    {
        [Required]
        [MinLength(1, ErrorMessage = "At least one image id is required.")]
        public List<int> ImageIds { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ImageIds.Count != ImageIds.Distinct().Count())
            {
                yield return new ValidationResult(
                    "ImageIds cannot contain duplicates.",
                    new[] { nameof(ImageIds) }
                );
            }
        }
    }
}
