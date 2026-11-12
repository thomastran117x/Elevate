using System.Security.Cryptography;
using System.Text;

using backend.main.application.environment;

using Microsoft.IdentityModel.Tokens;

namespace backend.main.application.security
{
    /// <summary>
    /// The HS256 keys for access tokens: one active key that signs, plus previous keys that are
    /// still accepted for validation while a rotation overlaps. Each key carries a <c>kid</c>
    /// derived from the secret, so a token names the key that signed it.
    /// </summary>
    /// <remarks>
    /// Rotate by moving the current secret to <c>JWT_SECRET_ACCESS_PREVIOUS</c> and setting a new
    /// <c>JWT_SECRET_ACCESS</c>; roll back by swapping them. Drop the previous key once every token
    /// it signed has expired (15 minutes plus clock skew). See docs/operations/key-rotation.md.
    /// The <c>kid</c> is a truncated SHA-256 of the secret. It reveals nothing a signed token does
    /// not already let an attacker test offline, and nothing at all about a high-entropy secret.
    /// </remarks>
    public sealed class JwtSigningKeyRing
    {
        /// <summary>HS256 needs at least a 256-bit key.</summary>
        public const int MinimumSecretBytes = 32;

        public JwtSigningKeyRing(string activeSecret, IEnumerable<string>? previousSecrets = null)
        {
            if (string.IsNullOrWhiteSpace(activeSecret))
                throw new ArgumentException("An active JWT signing secret is required.", nameof(activeSecret));

            SigningKey = CreateKey(activeSecret);

            var validationKeys = new List<SymmetricSecurityKey> { SigningKey };
            foreach (var previous in previousSecrets ?? [])
            {
                if (string.IsNullOrWhiteSpace(previous) || validationKeys.Any(key => key.KeyId == KeyIdFor(previous)))
                    continue;

                validationKeys.Add(CreateKey(previous));
            }

            ValidationKeys = validationKeys;
        }

        /// <summary>The key new access tokens are signed with.</summary>
        public SymmetricSecurityKey SigningKey
        {
            get;
        }

        /// <summary>Every key a presented access token may be signed with, the active key first.</summary>
        public IReadOnlyList<SymmetricSecurityKey> ValidationKeys
        {
            get;
        }

        public static JwtSigningKeyRing FromEnvironment() =>
            new(EnvironmentSetting.JwtSecretKeyAccess, EnvironmentSetting.JwtSecretKeyAccessPrevious);

        /// <summary>A stable, non-reversible key id: base64url of the first 8 bytes of SHA-256(secret).</summary>
        public static string KeyIdFor(string secret) =>
            Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(secret))[..8]);

        private static SymmetricSecurityKey CreateKey(string secret) =>
            new(Encoding.UTF8.GetBytes(secret))
            {
                KeyId = KeyIdFor(secret)
            };
    }
}
