namespace backend.main.features.auth.contracts.requests
{
    /// <summary>
    /// Refresh-session payload for API-token clients.
    /// </summary>
    public class RefreshTokenRequest
    {
        /// <summary>
        /// Refresh token, usually supplied by API clients when cookie transport is not used.
        /// </summary>
        public string? RefreshToken
        {
            get; set;
        }
        /// <summary>
        /// Session binding token paired with the refresh token for API-token transport.
        /// </summary>
        public string? SessionBindingToken
        {
            get; set;
        }
    }
}
