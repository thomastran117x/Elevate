using System.Security.Cryptography;

using backend.main.shared.utilities.logger;

namespace backend.main.features.auth.mfa.totp
{
    /// <summary>What one pass of the TOTP key-rotation sweep did.</summary>
    /// <param name="ReEncrypted">Secrets moved under the active key.</param>
    /// <param name="ChangedConcurrently">Secrets re-enrolled or removed while the pass ran; left alone.</param>
    /// <param name="Undecryptable">Secrets no configured key can decrypt; left alone and reported.</param>
    /// <param name="Remaining">Secrets still under a non-active key after the pass.</param>
    public sealed record TotpKeyRotationResult(int ReEncrypted, int ChangedConcurrently, int Undecryptable, int Remaining);

    /// <summary>
    /// Re-encrypts stored TOTP secrets under the active key while previous keys are configured.
    /// <para>
    /// Each row is read without tracking, decrypted with whichever key it needs, encrypted under the
    /// active key, and written back with a conditional update that only matches the exact value
    /// read. Replicas can therefore run the sweep at once, and a user who re-enrolls or removes TOTP
    /// mid-pass keeps their change. Secrets are never logged; only counts are.
    /// </para>
    /// <para>
    /// Split from <see cref="TotpKeyRotationService"/> so the work is a plain scoped class that
    /// tests can run directly.
    /// </para>
    /// </summary>
    public sealed class TotpKeyRotationRunner
    {
        internal const int BatchSize = 200;
        internal const int MaxBatchesPerRun = 50;

        private readonly ITotpMfaEnrollmentRepository _repository;
        private readonly TotpSecretProtector _protector;

        public TotpKeyRotationRunner(ITotpMfaEnrollmentRepository repository, TotpSecretProtector protector)
        {
            _repository = repository;
            _protector = protector;
        }

        public async Task<TotpKeyRotationResult> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            if (!_protector.HasPreviousKeys)
                return new TotpKeyRotationResult(0, 0, 0, 0);

            var prefix = _protector.ActivePrefix;
            var reEncrypted = 0;
            var changedConcurrently = 0;
            var undecryptable = 0;
            var afterUserId = 0;

            // Bounded rather than "until empty", so a large backlog does not hold one scoped
            // DbContext open indefinitely; whatever is left waits for the next pass.
            for (var batch = 0; batch < MaxBatchesPerRun; batch++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rows = await _repository.ListSecretsNotUnderKeyAsync(prefix, afterUserId, BatchSize, cancellationToken);
                foreach (var row in rows)
                {
                    afterUserId = row.UserId;

                    byte[] secret;
                    try
                    {
                        secret = _protector.Unprotect(row.EncryptedSecret);
                    }
                    catch (CryptographicException)
                    {
                        undecryptable++;
                        continue;
                    }

                    try
                    {
                        var replaced = await _repository.ReplaceSecretAsync(
                            row.UserId,
                            row.EncryptedSecret,
                            _protector.Protect(secret),
                            TotpSecretProtector.FormatVersion,
                            cancellationToken);

                        if (replaced)
                            reEncrypted++;
                        else
                            changedConcurrently++;
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(secret);
                    }
                }

                if (rows.Count < BatchSize)
                    break;
            }

            var remaining = await _repository.CountSecretsNotUnderKeyAsync(prefix, cancellationToken);
            var result = new TotpKeyRotationResult(reEncrypted, changedConcurrently, undecryptable, remaining);

            if (reEncrypted > 0 || undecryptable > 0 || remaining > 0)
            {
                Logger.Info(
                    $"[TotpKeyRotation] Active key {_protector.ActiveKeyId}: re-encrypted {reEncrypted}, "
                    + $"{changedConcurrently} changed concurrently, {undecryptable} undecryptable, {remaining} still under a previous key.");
            }

            if (undecryptable > 0)
            {
                Logger.Warn(
                    $"[TotpKeyRotation] {undecryptable} TOTP secrets cannot be decrypted with any configured key. "
                    + "Do not retire a previous key until this is resolved.");
            }

            return result;
        }
    }
}
