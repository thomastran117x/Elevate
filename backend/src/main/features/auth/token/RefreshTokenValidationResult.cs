namespace backend.main.features.auth.token
{
    public class RefreshTokenValidationResult
    {
        public required string SessionId
        {
            get; init;
        }
        public required int UserId
        {
            get; init;
        }
        /// <summary>
        /// The AuthVersion the session was issued under, or null for a session stored before
        /// the version was recorded.
        /// </summary>
        public int? AuthVersion
        {
            get; init;
        }
        public required SessionTransport Transport
        {
            get; init;
        }
    }
}
