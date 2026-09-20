using System.ComponentModel.DataAnnotations;

using backend.main.shared.storage;

namespace backend.main.shared.attributes.validation
{
    /// <summary>
    /// Rejects an upload whose leading bytes are not a supported image format. The declared
    /// Content-Type and the multipart file name only describe what the caller claims to be
    /// sending, so this is the check a renamed script cannot talk its way past.
    /// </summary>
    /// <remarks>
    /// This reads magic bytes only, so it stops a file that is not an image at all — it does not
    /// stop a file that is a real image and also something else. A payload carrying a genuine
    /// GIF89a header is a genuine GIF and passes here. Catching that needs full decode and
    /// re-encode, which is the separately tracked media-worker and quarantine work.
    /// </remarks>
    public class ImageContentAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not IFormFile file)
                return ValidationResult.Success;

            // TryDetect opens its own stream and restores the position, so the action and the blob
            // service can still read the file from the beginning afterwards.
            if (!ImageSignatureInspector.TryDetect(file, out _))
                return new ValidationResult("The file content must be a JPEG, PNG, WEBP, or GIF image.");

            return ValidationResult.Success;
        }
    }
}
