using System.ComponentModel.DataAnnotations;

using backend.main.shared.storage;

namespace backend.main.shared.attributes.validation
{
    /// <summary>
    /// Rejects an upload whose bytes are not a supported image. AllowedExtensions and the declared
    /// Content-Type only describe what the caller claims to be sending, so this is the check a
    /// renamed script or an HTML polyglot cannot talk its way past.
    /// </summary>
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
