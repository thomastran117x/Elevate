using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.contracts.requests
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

            var duplicateIds = Ids
                .GroupBy(id => id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateIds.Count > 0)
            {
                yield return new ValidationResult(
                    $"Duplicate event IDs are not allowed: {string.Join(", ", duplicateIds)}.",
                    new[] { nameof(Ids) });
            }
        }
    }
}

