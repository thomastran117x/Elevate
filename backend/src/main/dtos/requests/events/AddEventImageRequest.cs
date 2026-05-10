using System.ComponentModel.DataAnnotations;

namespace backend.main.dtos.requests.events
{
    public class AddEventImageRequest : IValidatableObject
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
}
