namespace backend.main.shared.responses
{
    /// <summary>
    /// Standard pagination payload used inside API response envelopes.
    /// </summary>
    public class PagedResponse<T>
    {
        /// <summary>
        /// Items for the current page.
        /// </summary>
        public IEnumerable<T> Items
        {
            get; set;
        }
        /// <summary>
        /// Total number of matching records.
        /// </summary>
        public int TotalCount
        {
            get; set;
        }
        /// <summary>
        /// Current 1-based page number.
        /// </summary>
        public int Page
        {
            get; set;
        }
        /// <summary>
        /// Requested page size.
        /// </summary>
        public int PageSize
        {
            get; set;
        }
        /// <summary>
        /// Total number of available pages.
        /// </summary>
        public int TotalPages
        {
            get; set;
        }

        public PagedResponse(IEnumerable<T> items, int totalCount, int page, int pageSize)
        {
            Items = items;
            TotalCount = totalCount;
            Page = page;
            PageSize = pageSize;
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        }
    }
}
