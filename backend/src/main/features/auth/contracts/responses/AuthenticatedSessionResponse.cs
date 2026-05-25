namespace backend.main.features.auth.contracts.responses
{
    /// <summary>
    /// Session tokens returned after successful authentication or verification flows.
    /// </summary>
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

        /// <summary>
        /// Bearer access token used for authenticated API requests.
        /// </summary>
        public string AccessToken
        {
            get; set;
        }
        /// <summary>
        /// UTC timestamp when the access token expires.
        /// </summary>
        public DateTime ExpiresAtUtc
        {
            get; set;
        }
        /// <summary>
        /// Refresh token for API-token clients; browser clients receive this via secure cookies instead.
        /// </summary>
        public string? RefreshToken
        {
            get; set;
        }
        /// <summary>
        /// Session binding token paired with the refresh token for API-token clients.
        /// </summary>
        public string? SessionBindingToken
        {
            get; set;
        }
    }
}
