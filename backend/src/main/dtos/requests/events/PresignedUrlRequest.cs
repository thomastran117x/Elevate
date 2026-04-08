using System.ComponentModel.DataAnnotations;

namespace backend.main.dtos.requests.events
{
    public class PresignedUrlRequest
    {
        [Required]
        public string FileName { get; set; } = null!;

        [Required]
        public string ContentType { get; set; } = null!;
    }
}
