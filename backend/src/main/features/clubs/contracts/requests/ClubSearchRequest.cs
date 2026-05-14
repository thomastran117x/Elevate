using System.ComponentModel.DataAnnotations;

using backend.main.features.clubs.search;

namespace backend.main.features.clubs.contracts.requests
{
    public sealed record ClubSearchRequest : IValidatableObject
    {
        public string? Query { get; init; }
        public ClubSearchFilters? Filters { get; init; }
        public ClubSortBy SortBy { get; init; } = ClubSortBy.Relevance;

        [Range(1, int.MaxValue, ErrorMessage = "page must be at least 1.")]
        public int Page { get; init; } = 1;

        [Range(1, 100, ErrorMessage = "pageSize must be between 1 and 100.")]
        public int PageSize { get; init; } = 20;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            foreach (var error in PublicClubSearchCriteriaFactory.ValidateRequest(this))
            {
                yield return new ValidationResult(error.Message, [error.MemberName]);
            }
        }
    }

    public sealed record ClubSearchFilters
    {
        public ClubType? ClubType { get; init; }
    }
}
