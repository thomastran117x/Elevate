using System.ComponentModel.DataAnnotations;

using backend.main.shared.attributes.validation;

namespace backend.main.features.profile.contracts.requests
{
    public class AvatarUploadRequest
    {
        /// <summary>The largest avatar file accepted, and the limit the API advertises.</summary>
        public const int MaxImageBytes = 5 * 1024 * 1024;

        /// <summary>
        /// The per-request cap. It has to exceed <see cref="MaxImageBytes"/> by enough to cover the
        /// multipart envelope — boundary lines, Content-Disposition and Content-Type headers — or a
        /// file at exactly the advertised limit makes the request larger than the cap and Kestrel
        /// rejects it with a bare 413 before <see cref="MaxFileSizeAttribute"/> ever runs. The
        /// headroom keeps the file-level validator the thing that decides, so an oversized upload
        /// gets the validation message rather than a transport error.
        /// </summary>
        public const int MaxRequestBytes = MaxImageBytes + (8 * 1024);

        [Required]
        [MaxFileSize(MaxImageBytes)]
        [AllowedExtensions(new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" })]
        [ImageContent]
        public required IFormFile Image
        {
            get; set;
        }
    }
}
