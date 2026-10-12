using System.Text;

namespace backend.main.features.auth.password
{
    /// <summary>
    /// The single Unicode normalization applied to a password before it is measured or hashed, so
    /// the same passphrase typed on different keyboards or input methods counts and hashes the same.
    /// </summary>
    public static class PasswordText
    {
        /// <summary>
        /// NFKC, as NIST SP 800-63B recommends. A string that cannot be normalized (it carries a lone
        /// surrogate) is returned unchanged: UTF-8 encoding replaces those deterministically, so
        /// the same input still hashes the same way every time.
        /// </summary>
        public static string Normalize(string password)
        {
            try
            {
                return password.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                return password;
            }
        }

        /// <summary>
        /// The length a person would count: Unicode scalar values of the normalized form, so an
        /// emoji or an accented letter is one character rather than two UTF-16 units.
        /// </summary>
        public static int Length(string password)
        {
            var count = 0;
            foreach (var _ in Normalize(password).EnumerateRunes())
                count++;

            return count;
        }
    }
}
