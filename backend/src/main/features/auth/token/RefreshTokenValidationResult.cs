namespace backend.main.features.auth.token
{
    public class RefreshTokenValidationResult
    {
        public required string SessionId { get; init; }
        public required int UserId { get; init; }
        public required SessionTransport Transport { get; init; }
    }
}
