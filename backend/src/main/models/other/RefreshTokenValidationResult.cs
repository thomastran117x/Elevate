namespace backend.main.models.other
{
    public class RefreshTokenValidationResult
    {
        public required string SessionId { get; init; }
        public required int UserId { get; init; }
    }
}
