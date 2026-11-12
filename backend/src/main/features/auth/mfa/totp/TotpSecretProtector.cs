using System.Security.Cryptography;

using backend.main.application.environment;

using Microsoft.IdentityModel.Tokens;

namespace backend.main.features.auth.mfa.totp
{
    /// <summary>
    /// Encrypts TOTP shared secrets at rest with AES-256-GCM under a key ring: one active key that
    /// encrypts, plus previous keys that can still decrypt while a rotation is under way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The current format is <c>v2:{kid}:{base64(nonce‖tag‖ciphertext)}</c>, where <c>kid</c> is
    /// base64url of the first 8 bytes of SHA-256 of the key, so each secret names the key it needs.
    /// The original <c>v1:{base64(...)}</c> format names no key; it is tried against every key,
    /// active first, and the GCM tag rejects the wrong ones.
    /// </para>
    /// <para>
    /// Rotate in three deployments, because <see cref="TotpKeyRotationService"/> on the first
    /// replica with a new active key rewrites the shared rows at once, and every replica must
    /// already be able to decrypt them:
    /// <list type="number">
    /// <item>Add the new key to <c>AUTH_TOTP_ENCRYPTION_KEYS_PREVIOUS</c>.</item>
    /// <item>Swap it into <c>AUTH_TOTP_ENCRYPTION_KEY</c>, keeping the old one as previous.</item>
    /// <item>Remove the old one once no stored secret still needs it.</item>
    /// </list>
    /// Roll back from the second deployment by swapping the two. See docs/operations/key-rotation.md.
    /// </para>
    /// </remarks>
    public sealed class TotpSecretProtector
    {
        /// <summary>The stored format version for secrets this protector writes.</summary>
        public const int FormatVersion = 2;

        private const int KeySizeBytes = 32;
        private const int TagSizeBytes = 16;
        private const int NonceSizeBytes = 12;
        private const string LegacyPrefix = "v1:";
        private const string KeyedPrefix = "v2:";

        private readonly IReadOnlyList<EncryptionKey> _keys;

        public TotpSecretProtector(string activeKeyBase64, IEnumerable<string>? previousKeysBase64 = null)
        {
            var active = EncryptionKey.Parse(activeKeyBase64);
            var keys = new List<EncryptionKey> { active };

            foreach (var previous in previousKeysBase64 ?? [])
            {
                if (string.IsNullOrWhiteSpace(previous))
                    continue;

                var key = EncryptionKey.Parse(previous);
                if (keys.All(existing => existing.Id != key.Id))
                    keys.Add(key);
            }

            _keys = keys;
        }

        /// <summary>The id of the key new secrets are encrypted with.</summary>
        public string ActiveKeyId => _keys[0].Id;

        /// <summary>Every stored secret encrypted under the active key starts with this.</summary>
        public string ActivePrefix => $"{KeyedPrefix}{ActiveKeyId}:";

        /// <summary>Whether a rotation is under way, so stored secrets may need re-encrypting.</summary>
        public bool HasPreviousKeys => _keys.Count > 1;

        public static TotpSecretProtector FromEnvironment() =>
            new(EnvironmentSetting.TotpEncryptionKey, EnvironmentSetting.TotpEncryptionKeysPrevious);

        /// <summary>The key id for a base64 key: base64url of the first 8 bytes of SHA-256(key).</summary>
        public static string KeyIdFor(string keyBase64) => EncryptionKey.Parse(keyBase64).Id;

        public bool IsProtectedWithActiveKey(string protectedSecret) =>
            protectedSecret.StartsWith(ActivePrefix, StringComparison.Ordinal);

        public string Protect(byte[] secret)
        {
            var key = _keys[0];
            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            var ciphertext = new byte[secret.Length];
            var tag = new byte[TagSizeBytes];

            using (var aes = new AesGcm(key.Material, TagSizeBytes))
                aes.Encrypt(nonce, secret, ciphertext, tag);

            var combined = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
            nonce.CopyTo(combined, 0);
            tag.CopyTo(combined, NonceSizeBytes);
            ciphertext.CopyTo(combined, NonceSizeBytes + TagSizeBytes);

            return $"{KeyedPrefix}{key.Id}:{Convert.ToBase64String(combined)}";
        }

        /// <exception cref="CryptographicException">
        /// The secret is malformed, names a key that is not configured, or no configured key
        /// authenticates it. Decryption fails closed; nothing is returned.
        /// </exception>
        public byte[] Unprotect(string protectedSecret)
        {
            if (protectedSecret.StartsWith(KeyedPrefix, StringComparison.Ordinal))
            {
                var separator = protectedSecret.IndexOf(':', KeyedPrefix.Length);
                if (separator < 0)
                    throw new CryptographicException("Malformed TOTP secret.");

                var keyId = protectedSecret[KeyedPrefix.Length..separator];
                var key = _keys.FirstOrDefault(candidate => candidate.Id == keyId)
                    ?? throw new CryptographicException("The TOTP secret was encrypted with a key that is not configured.");

                return Decrypt(key, Decode(protectedSecret[(separator + 1)..]));
            }

            if (protectedSecret.StartsWith(LegacyPrefix, StringComparison.Ordinal))
            {
                var combined = Decode(protectedSecret[LegacyPrefix.Length..]);
                foreach (var key in _keys)
                {
                    try
                    {
                        return Decrypt(key, combined);
                    }
                    catch (AuthenticationTagMismatchException)
                    {
                        // Not this key; try the next one.
                    }
                }

                throw new CryptographicException("No configured key decrypts the TOTP secret.");
            }

            throw new CryptographicException("Unknown TOTP secret encryption version.");
        }

        private static byte[] Decrypt(EncryptionKey key, byte[] combined)
        {
            if (combined.Length < NonceSizeBytes + TagSizeBytes)
                throw new CryptographicException("Malformed TOTP secret.");

            var nonce = combined[..NonceSizeBytes];
            var tag = combined[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
            var ciphertext = combined[(NonceSizeBytes + TagSizeBytes)..];
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(key.Material, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }

        private static byte[] Decode(string base64)
        {
            try
            {
                return Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                throw new CryptographicException("Malformed TOTP secret.");
            }
        }

        private sealed record EncryptionKey(string Id, byte[] Material)
        {
            public static EncryptionKey Parse(string keyBase64)
            {
                byte[] material;
                try
                {
                    material = Convert.FromBase64String(keyBase64);
                }
                catch (FormatException)
                {
                    throw new InvalidOperationException("A TOTP encryption key is not valid base64.");
                }

                if (material.Length != KeySizeBytes)
                    throw new InvalidOperationException("A TOTP encryption key must decode to exactly 32 bytes.");

                return new EncryptionKey(Base64UrlEncoder.Encode(SHA256.HashData(material)[..8]), material);
            }
        }
    }
}
