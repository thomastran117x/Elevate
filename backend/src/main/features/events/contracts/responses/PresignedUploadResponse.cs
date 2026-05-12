namespace backend.main.features.events.contracts.responses
{
    public class PresignedUploadResponse
    {
        public string UploadUrl { get; set; } = null!;
        public string PublicUrl { get; set; } = null!;
        public DateTimeOffset ExpiresAt { get; set; }
    }
}

