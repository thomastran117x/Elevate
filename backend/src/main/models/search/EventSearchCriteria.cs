using backend.main.models.enums;

namespace backend.main.models.search
{
    public sealed record EventSearchCriteria
    {
        public string? Query { get; init; }
        public int? ClubId { get; init; }
        public bool IsPrivate { get; init; }
        public EventStatus? Status { get; init; }
        public EventCategory? Category { get; init; }
        public List<string>? Tags { get; init; }
        public string? City { get; init; }
        public double? Lat { get; init; }
        public double? Lng { get; init; }
        public double? RadiusKm { get; init; }
        public EventSortBy SortBy { get; init; } = EventSortBy.Relevance;
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 20;
    }
}
