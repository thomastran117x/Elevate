using backend.main.shared.exceptions.http;

namespace backend.main.features.events.search
{
    public static class EventSearchFallbackSupport
    {
        public static bool RequiresDatabaseFallback(EventSearchCriteria criteria)
        {
            if (string.IsNullOrWhiteSpace(criteria.Query))
                return false;
            return criteria.Query.Contains('%')
                || criteria.Query.Contains('_')
                || criteria.Query.Contains('\\');
        }
        public static void EnsureSupported(EventSearchCriteria criteria)
        {
            if (criteria.Tags != null && criteria.Tags.Count > 0)
            {
                throw new NotAvailableException(
                    "Tag filtering is temporarily unavailable because search indexing is unavailable.");
            }

            if (criteria.Lat.HasValue || criteria.Lng.HasValue || criteria.RadiusKm.HasValue)
            {
                throw new NotAvailableException(
                    "Location-based filtering is temporarily unavailable because search indexing is unavailable.");
            }

            if (criteria.SortBy == EventSortBy.Distance)
            {
                throw new NotAvailableException(
                    "Distance sorting is temporarily unavailable because search indexing is unavailable.");
            }
        }
    }
}
