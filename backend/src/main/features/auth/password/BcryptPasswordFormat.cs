namespace backend.main.features.auth.password
{
    /// <summary>
    /// The modular-crypt BCrypt hashes (<c>$2a$</c>, <c>$2b$</c>, <c>$2x$</c>, <c>$2y$</c>) every
    /// account stored before Argon2id. They were made from the password exactly as typed, so they
    /// are verified that way too, without the normalization newer formats apply.
    /// </summary>
    internal sealed class BcryptPasswordFormat
    {
        private readonly int _workFactor;

        public BcryptPasswordFormat(int workFactor)
        {
            _workFactor = workFactor;
        }

        /// <summary>
        /// The exact modular-crypt shape, <c>$2?$NN$</c> plus 53 characters of salt and digest.
        /// BCrypt.Net throws assorted exceptions on anything shorter, so those never reach it.
        /// </summary>
        public static bool Matches(string storedHash) =>
            storedHash.Length == 60
            && storedHash.StartsWith("$2", StringComparison.Ordinal)
            && storedHash[2] is 'a' or 'b' or 'x' or 'y'
            && storedHash[3] == '$'
            && char.IsAsciiDigit(storedHash[4])
            && char.IsAsciiDigit(storedHash[5])
            && storedHash[6] == '$';

        public string Hash(string password) =>
            BCrypt.Net.BCrypt.HashPassword(password, workFactor: _workFactor);

        public bool Verify(string password, string storedHash)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, storedHash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }

        public bool HasCurrentParameters(string storedHash)
        {
            try
            {
                return !BCrypt.Net.BCrypt.PasswordNeedsRehash(storedHash, _workFactor);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }
    }
}
