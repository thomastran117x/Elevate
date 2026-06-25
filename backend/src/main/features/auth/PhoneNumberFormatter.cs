using System.Text.RegularExpressions;

using backend.main.shared.exceptions.http;

namespace backend.main.features.auth
{
    internal static class PhoneNumberFormatter
    {
        private static readonly Regex NonDigits = new("[^0-9+]", RegexOptions.Compiled);

        internal static string Normalize(string rawPhoneNumber)
        {
            if (string.IsNullOrWhiteSpace(rawPhoneNumber))
                throw new BadRequestException("Phone number is required.");

            var trimmed = rawPhoneNumber.Trim();
            var cleaned = NonDigits.Replace(trimmed, string.Empty);

            if (cleaned.StartsWith('+'))
            {
                var digits = cleaned[1..];
                if (digits.Length is < 10 or > 15 || digits.Any(ch => !char.IsDigit(ch)))
                    throw new BadRequestException("Phone number must be a valid international number.");

                return $"+{digits}";
            }

            var localDigits = new string(cleaned.Where(char.IsDigit).ToArray());
            if (localDigits.Length == 10)
                return $"+1{localDigits}";

            if (localDigits.Length == 11 && localDigits.StartsWith('1'))
                return $"+{localDigits}";

            if (localDigits.Length is >= 10 and <= 15)
                return $"+{localDigits}";

            throw new BadRequestException("Phone number must be a valid mobile number.");
        }

        internal static string Mask(string phoneNumber)
        {
            var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
            if (digits.Length <= 4)
                return phoneNumber;

            return $"***-***-{digits[^4..]}";
        }
    }
}
