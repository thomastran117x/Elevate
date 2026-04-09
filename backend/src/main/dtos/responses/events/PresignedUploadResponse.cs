namespace backend.main.dtos.responses.events
{
    public class PresignedUploadResponse
    {
        public string UploadUrl { get; set; } = null!;
        public string PublicUrl { get; set; } = null!;
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
