namespace backend.main.features.auth.contracts.responses
{
    public class AuthenticatedSessionResponse
    {
        public AuthenticatedSessionResponse(
            string accessToken,
            DateTime expiresAtUtc,
            string? refreshToken = null,
            string? sessionBindingToken = null
        )
        {
            AccessToken = accessToken;
            ExpiresAtUtc = expiresAtUtc;
            RefreshToken = refreshToken;
            SessionBindingToken = sessionBindingToken;
        }

        public string AccessToken
        {
            get; set;
        }
        public DateTime ExpiresAtUtc
        {
            get; set;
        }
        public string? RefreshToken
        {
            get; set;
        }
        public string? SessionBindingToken
        {
            get; set;
        }
    }
}
