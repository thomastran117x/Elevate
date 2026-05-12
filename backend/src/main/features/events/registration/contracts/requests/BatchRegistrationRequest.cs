using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.registration.contracts.requests
{
    public class BatchRegistrationRequest : IValidatableObject
    {
        [Required]
        public List<int> EventIds { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (EventIds.Count == 0 || EventIds.Count > 20)
            {
                yield return new ValidationResult(
                    "Batch size must be between 1 and 20 event IDs.",
                    new[] { nameof(EventIds) });
            }
        }
    }
}


