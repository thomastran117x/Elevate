using System.ComponentModel.DataAnnotations;

using backend.main.shared.attributes.validation;

namespace backend.main.features.profile.contracts.requests
{
    public class AvatarUploadRequest
    {
        [Required]
        [MaxFileSize(5 * 1024 * 1024)]
        [AllowedExtensions(new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" })]
        public required IFormFile Image
        {
            get; set;
        }
    }
}
