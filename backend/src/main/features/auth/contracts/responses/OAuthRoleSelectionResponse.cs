namespace backend.main.features.auth.contracts.responses
{
    public sealed class OAuthRoleSelectionResponse
    {
        public required string SignupToken
        {
            get; set;
        }
        public required string Email
        {
            get; set;
        }
        public required string Name
        {
            get; set;
        }
        public required string Provider
        {
            get; set;
        }
    }
}
