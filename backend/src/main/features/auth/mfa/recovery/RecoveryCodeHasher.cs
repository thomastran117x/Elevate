using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using backend.main.features.auth.password;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.mfa.recovery
{
    /// <summary>
    /// Hashes recovery codes like passwords (Argon2id, per ADR 0001) with the recovery-code pepper
    /// as Argon2's secret input and the owning account as its associated data, so a hash verifies
    /// only with the pepper and only for its own account.
    /// </summary>
    /// <remarks>
    /// A code is found by its <see cref="Selector"/>, a keyed HMAC, so every check runs exactly
    /// one Argon2id derivation: against the matching row, or against a dummy hash when none
    /// matches. A miss therefore costs the same as a hit, and an attempt never costs ten.
    /// </remarks>
    public sealed class RecoveryCodeHasher
    {
        /// <summary>Hex characters kept from the selector digest: 128 bits.</summary>
        internal const int SelectorLength = 32;

        private readonly byte[] _pepper;
        private readonly Argon2idPasswordFormat _argon2id;
        private readonly Lazy<string> _dummyHash;

        public RecoveryCodeHasher(
            IOptions<RecoveryCodeOptions> options,
            IOptions<PasswordPolicyOptions> passwordOptions)
        {
            _pepper = options.Value.PepperBytes();
            _argon2id = new Argon2idPasswordFormat(passwordOptions.Value.Hashing.Argon2, _pepper);
            _dummyHash = new Lazy<string>(
                () => Hash(0, RecoveryCodeFormat.GenerateSet()[0]),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public string Selector(int userId, string normalizedCode)
        {
            var material = Encoding.UTF8.GetBytes(
                string.Create(CultureInfo.InvariantCulture, $"recovery-code-selector|v1|{userId}|{normalizedCode}"));
            var digest = HMACSHA256.HashData(_pepper, material);
            return Convert.ToHexString(digest)[..SelectorLength];
        }

        public string Hash(int userId, string normalizedCode) =>
            _argon2id.Hash(normalizedCode, AssociatedData(userId));

        /// <summary>
        /// Checks the code against <paramref name="storedHash"/>. With no stored hash it still
        /// runs one derivation, against a dummy, and fails.
        /// </summary>
        public bool Verify(int userId, string normalizedCode, string? storedHash)
        {
            if (storedHash is null)
            {
                _ = _argon2id.Verify(normalizedCode, _dummyHash.Value, AssociatedData(0));
                return false;
            }

            return _argon2id.Verify(normalizedCode, storedHash, AssociatedData(userId));
        }

        private static byte[] AssociatedData(int userId) =>
            Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"recovery-code|v1|{userId}"));
    }
}
