using System.Security.Cryptography;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.password
{
    /// <summary>
    /// Picks the stored hash's format from its prefix, so legacy BCrypt and current Argon2id hashes
    /// can live in the same column for as long as accounts take to log in and be upgraded.
    /// </summary>
    public sealed class PasswordHasher : IPasswordHasher
    {
        private readonly PasswordHashAlgorithm _algorithm;
        private readonly Argon2idPasswordFormat _argon2id;
        private readonly BcryptPasswordFormat _bcrypt;
        private readonly Lazy<string> _dummyHash;

        public PasswordHasher(IOptions<PasswordPolicyOptions> options)
        {
            var hashing = options.Value.Hashing;
            _algorithm = hashing.Algorithm;
            _argon2id = new Argon2idPasswordFormat(hashing.Argon2);
            _bcrypt = new BcryptPasswordFormat(hashing.BcryptWorkFactor);
            _dummyHash = new Lazy<string>(
                () => Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(16))),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public string DummyHash => _dummyHash.Value;

        public string Hash(string password) =>
            _algorithm == PasswordHashAlgorithm.Bcrypt
                ? _bcrypt.Hash(password)
                : _argon2id.Hash(password);

        public PasswordVerificationResult Verify(string password, string? storedHash)
        {
            if (string.IsNullOrEmpty(storedHash))
                return PasswordVerificationResult.Failed;

            if (Argon2idPasswordFormat.Matches(storedHash))
            {
                if (!_argon2id.Verify(password, storedHash))
                    return PasswordVerificationResult.Failed;

                return _algorithm == PasswordHashAlgorithm.Argon2id && _argon2id.HasCurrentParameters(storedHash)
                    ? PasswordVerificationResult.Success
                    : PasswordVerificationResult.SuccessRehashNeeded;
            }

            if (BcryptPasswordFormat.Matches(storedHash))
            {
                if (!_bcrypt.Verify(password, storedHash))
                    return PasswordVerificationResult.Failed;

                return _algorithm == PasswordHashAlgorithm.Bcrypt && _bcrypt.HasCurrentParameters(storedHash)
                    ? PasswordVerificationResult.Success
                    : PasswordVerificationResult.SuccessRehashNeeded;
            }

            return PasswordVerificationResult.Failed;
        }
    }
}
