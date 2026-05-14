namespace backend.main.features.clubs.search
{
    public sealed record ClubSearchCriteria
    {
        public string? Query { get; init; }
        public ClubType? ClubType { get; init; }
        public ClubSortBy SortBy { get; init; } = ClubSortBy.Relevance;
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 20;
    }
}
