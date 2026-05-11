using System.ComponentModel.DataAnnotations;

namespace backend.main.shared.attributes.validation
{
    public class MaxFileSizeAttribute : ValidationAttribute
    {
        private readonly int _maxBytes;

        public MaxFileSizeAttribute(int maxBytes)
        {
            _maxBytes = maxBytes;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not IFormFile file)
                return ValidationResult.Success;

            if (file.Length > _maxBytes)
                return new ValidationResult($"File size must be less than {_maxBytes / 1024 / 1024}MB");

            return ValidationResult.Success;
        }
    }
}
