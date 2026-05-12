using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.contracts.requests
{
    public class PresignedUrlRequest : IValidatableObject
    {
        [Range(1, int.MaxValue, ErrorMessage = "ClubId must be greater than 0.")]
        public int ClubId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "EventId must be greater than 0.")]
        public int? EventId { get; set; }

        [Required]
        [StringLength(255, MinimumLength = 3)]
        public string FileName { get; set; } = null!;

        [Required]
        [StringLength(100)]
        public string ContentType { get; set; } = null!;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (EventId.HasValue && ClubId <= 0)
            {
                yield return new ValidationResult(
                    "ClubId is required when EventId is provided.",
                    new[] { nameof(ClubId), nameof(EventId) }
                );
            }
        }
    }
}

