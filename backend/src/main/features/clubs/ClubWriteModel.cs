namespace backend.main.features.clubs
{
    /// <summary>
    /// The mutable club fields shared by create and update. The controller maps the request DTO
    /// into this so the service takes one payload instead of a long positional parameter list.
    /// </summary>
    public sealed class ClubWriteModel
    {
        public required string Name
        {
            get; init;
        }
        public required string Description
        {
            get; init;
        }
        public required string Clubtype
        {
            get; init;
        }
        public required string ClubImageUrl
        {
            get; init;
        }
        public string? BannerImageUrl
        {
            get; init;
        }
        public List<string>? GalleryImageUrls
        {
            get; init;
        }
        public string? Phone
        {
            get; init;
        }
        public string? Email
        {
            get; init;
        }
        public string? WebsiteUrl
        {
            get; init;
        }
        public string? Location
        {
            get; init;
        }
        public int? MaxMemberCount
        {
            get; init;
        }
        public bool IsPrivate
        {
            get; init;
        }
    }
}
