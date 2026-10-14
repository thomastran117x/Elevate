namespace backend.main.features.auth.oauth
{
    public class OAuthUser
    {
        public string Id
        {
            get; set;
        }
        /// <summary>
        /// The address the provider reported, or null when it reported none. A provider subject
        /// that is already linked signs in without one.
        /// </summary>
        public string? Email
        {
            get; set;
        }
        /// <summary>
        /// Whether the provider proved the caller controls <see cref="Email"/>. Only a verified
        /// address may link the provider subject to an existing account by email.
        /// </summary>
        public bool EmailVerified
        {
            get; set;
        }
        public string Name
        {
            get; set;
        }
        public string Provider
        {
            get; set;
        }
        public OAuthUser(
            string Id,
            string? Email,
            string Name,
            string Provider,
            bool emailVerified = false)
        {
            this.Id = Id;
            this.Email = Email;
            this.Name = Name;
            this.Provider = Provider;
            EmailVerified = emailVerified;
        }
    }
}
