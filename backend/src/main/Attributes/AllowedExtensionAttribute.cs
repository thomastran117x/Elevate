using System.ComponentModel.DataAnnotations;

namespace backend.main.Attribute
{
    public class AllowedExtensionsAttribute : ValidationAttribute
    {
        private readonly string[] _extensions;

        public AllowedExtensionsAttribute(string[] extensions)
        {
            _extensions = extensions;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not IFormFile file)
                return ValidationResult.Success;

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!_extensions.Contains(ext))
                return new ValidationResult($"Invalid file type. Allowed: {string.Join(", ", _extensions)}");

            return ValidationResult.Success;
        }
    }
}
