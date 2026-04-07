using System.ComponentModel.DataAnnotations;

namespace backend.main.dtos.requests.events
{
    public class BatchDeleteRequest : IValidatableObject
    {
        [Required]
        public List<int> Ids { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Ids.Count == 0 || Ids.Count > 50)
            {
                yield return new ValidationResult(
                    "Batch size must be between 1 and 50 IDs.",
                    new[] { nameof(Ids) });
            }
        }
    }
}
