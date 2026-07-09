namespace backend.main.features.profile.contracts.responses
{
    /// <summary>
    /// Full self-service profile for the authenticated user.
    /// </summary>
    public class MyProfileResponse
    {
        public int Id
        {
            get; set;
        }
        public string Email { get; set; } = null!;
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
        public string? Phone
        {
            get; set;
        }
        public string? Address
        {
            get; set;
        }
        public bool GoogleLinked
        {
            get; set;
        }
        public bool MicrosoftLinked
        {
            get; set;
        }
        public DateTime CreatedAtUtc
        {
            get; set;
        }
        public DateTime UpdatedAtUtc
        {
            get; set;
        }
    }
}
