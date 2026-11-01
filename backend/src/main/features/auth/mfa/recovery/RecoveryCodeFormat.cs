using System.Security.Cryptography;
using System.Text;

namespace backend.main.features.auth.mfa.recovery
{
    /// <summary>
    /// Recovery codes are 28 Crockford base32 characters (140 bits from a CSPRNG), shown as seven
    /// groups of four. The alphabet leaves out I, L, O, and U, and input is read leniently: case,
    /// spaces, and hyphens are ignored, and O, I, and L are read as the digits they resemble.
    /// </summary>
    public static class RecoveryCodeFormat
    {
        public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        public const int Length = 28;
        public const int SetSize = 10;
        private const int GroupSize = 4;

        /// <summary>Longest input worth normalizing; anything longer is refused unread.</summary>
        private const int MaxInputLength = 64;

        /// <summary>Bits of entropy per code: log2(32) per character.</summary>
        public const int EntropyBits = Length * 5;

        /// <summary>A fresh set of distinct codes, in normalized form.</summary>
        public static string[] GenerateSet()
        {
            var codes = new HashSet<string>(StringComparer.Ordinal);
            while (codes.Count < SetSize)
                codes.Add(RandomNumberGenerator.GetString(Alphabet, Length));
            return [.. codes];
        }

        /// <summary>The grouped form shown to the user, e.g. <c>ABCD-EFGH-…</c>.</summary>
        public static string Display(string normalized) =>
            string.Join('-', normalized.Chunk(GroupSize).Select(chunk => new string(chunk)));

        public static bool TryNormalize(string? input, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(input) || input.Length > MaxInputLength)
                return false;

            var builder = new StringBuilder(Length);
            foreach (var raw in input)
            {
                if (raw == '-' || char.IsWhiteSpace(raw))
                    continue;

                var c = char.ToUpperInvariant(raw) switch
                {
                    'O' => '0',
                    'I' or 'L' => '1',
                    var other => other,
                };
                if (!Alphabet.Contains(c))
                    return false;

                builder.Append(c);
            }

            if (builder.Length != Length)
                return false;

            normalized = builder.ToString();
            return true;
        }
    }
}
