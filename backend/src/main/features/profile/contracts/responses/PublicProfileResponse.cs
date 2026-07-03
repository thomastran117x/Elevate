namespace backend.main.features.profile.contracts.responses
{
    /// <summary>
    /// Publicly viewable profile fields for any user, keyed by username.
    /// Deliberately excludes email, phone, and address.
    /// </summary>
    public class PublicProfileResponse
    {
        public int Id
        {
            get; set;
        }
        public string Username { get; set; } = null!;
        public string? Name
        {
            get; set;
        }
        public string? Avatar
        {
            get; set;
        }
        public string Usertype { get; set; } = null!;
        public DateTime CreatedAtUtc
        {
            get; set;
        }
    }
}
