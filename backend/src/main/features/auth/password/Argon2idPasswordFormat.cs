using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Konscious.Security.Cryptography;

namespace backend.main.features.auth.password
{
    /// <summary>
    /// Argon2id hashes in the PHC string format:
    /// <c>$argon2id$v=19$m=&lt;KiB&gt;,t=&lt;passes&gt;,p=&lt;lanes&gt;$&lt;salt&gt;$&lt;hash&gt;</c>, with salt and
    /// hash in unpadded base64. The parameters travel with each hash, so changing the configuration
    /// never stops an existing hash from verifying.
    /// </summary>
    internal sealed class Argon2idPasswordFormat
    {
        public const string Prefix = "$argon2id$";
        private const int Version = 0x13;
        private const int SaltLength = 16;
        private const int HashLength = 32;

        private readonly Argon2Options _options;
        private readonly byte[]? _secret;

        /// <param name="secret">
        /// Argon2's optional secret input (a pepper). It is not written into the PHC string, so a
        /// hash made with one verifies only through a format holding the same secret.
        /// </param>
        public Argon2idPasswordFormat(Argon2Options options, byte[]? secret = null)
        {
            _options = options;
            _secret = secret;
        }

        public static bool Matches(string storedHash) =>
            storedHash.StartsWith(Prefix, StringComparison.Ordinal);

        /// <param name="associatedData">
        /// Argon2's optional associated data, binding the hash to a context such as its owner. The
        /// same value must be passed to <see cref="Verify"/>.
        /// </param>
        public string Hash(string password, byte[]? associatedData = null)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltLength);
            var hash = Derive(
                password, salt, _options.MemoryKiB, _options.Iterations, _options.Parallelism, HashLength,
                _secret, associatedData);

            return string.Create(CultureInfo.InvariantCulture,
                $"{Prefix}v={Version}$m={_options.MemoryKiB},t={_options.Iterations},p={_options.Parallelism}${Encode(salt)}${Encode(hash)}");
        }

        public bool Verify(string password, string storedHash, byte[]? associatedData = null)
        {
            if (!TryParse(storedHash, out var parsed))
                return false;

            var actual = Derive(
                password, parsed.Salt, parsed.MemoryKiB, parsed.Iterations, parsed.Parallelism, parsed.Hash.Length,
                _secret, associatedData);
            return CryptographicOperations.FixedTimeEquals(actual, parsed.Hash);
        }

        public bool HasCurrentParameters(string storedHash) =>
            TryParse(storedHash, out var parsed)
            && parsed.MemoryKiB == _options.MemoryKiB
            && parsed.Iterations == _options.Iterations
            && parsed.Parallelism == _options.Parallelism
            && parsed.Salt.Length == SaltLength
            && parsed.Hash.Length == HashLength;

        private static byte[] Derive(
            string password,
            byte[] salt,
            int memoryKiB,
            int iterations,
            int parallelism,
            int length,
            byte[]? secret,
            byte[]? associatedData)
        {
            var passwordBytes = Encoding.UTF8.GetBytes(PasswordText.Normalize(password));
            try
            {
                using var argon2 = new Argon2id(passwordBytes)
                {
                    Salt = salt,
                    MemorySize = memoryKiB,
                    Iterations = iterations,
                    DegreeOfParallelism = parallelism,
                };
                if (secret is not null)
                    argon2.KnownSecret = secret;
                if (associatedData is not null)
                    argon2.AssociatedData = associatedData;
                return argon2.GetBytes(length);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }

        /// <summary>
        /// Strict on purpose: anything outside what <see cref="Hash"/> writes, or parameters beyond
        /// the configured bounds, is rejected rather than run, so a corrupt row cannot make a login
        /// allocate gigabytes.
        /// </summary>
        internal static bool TryParse(string storedHash, out ParsedHash parsed)
        {
            parsed = default;
            var parts = storedHash.Split('$');
            if (parts.Length != 6 || parts[0].Length != 0 || parts[1] != "argon2id")
                return false;

            if (parts[2] != $"v={Version}")
                return false;

            var parameters = parts[3].Split(',');
            if (parameters.Length != 3
                || !TryReadParameter(parameters[0], "m=", out var memoryKiB)
                || !TryReadParameter(parameters[1], "t=", out var iterations)
                || !TryReadParameter(parameters[2], "p=", out var parallelism))
            {
                return false;
            }

            if (memoryKiB is < 8 or > Argon2Options.MaxMemoryKiB
                || iterations is < 1 or > Argon2Options.MaxIterations
                || parallelism is < 1 or > Argon2Options.MaxParallelism
                || memoryKiB < 8 * parallelism)
            {
                return false;
            }

            if (!TryDecode(parts[4], out var salt) || salt.Length < 8
                || !TryDecode(parts[5], out var hash) || hash.Length < 16)
            {
                return false;
            }

            parsed = new ParsedHash(memoryKiB, iterations, parallelism, salt, hash);
            return true;
        }

        private static bool TryReadParameter(string segment, string key, out int value)
        {
            value = 0;
            return segment.StartsWith(key, StringComparison.Ordinal)
                && int.TryParse(
                    segment.AsSpan(key.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out value);
        }

        private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

        private static bool TryDecode(string value, out byte[] bytes)
        {
            bytes = [];
            if (value.Length == 0 || value.Contains('='))
                return false;

            var padded = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
            var buffer = new byte[padded.Length * 3 / 4];
            if (!Convert.TryFromBase64String(padded, buffer, out var written))
                return false;

            bytes = buffer[..written];
            return true;
        }

        internal readonly record struct ParsedHash(
            int MemoryKiB, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);
    }
}
