using backend.main.shared.attributes.repository;

namespace backend.main.features.auth.mfa.recovery
{
    public interface IRecoveryCodeRepository
    {
        Task<RecoveryCodeStatus> GetStatusAsync(int userId);

        Task<bool> HasUnusedAsync(int userId);

        /// <summary>The unused code with this selector in the account's set, if any.</summary>
        Task<RecoveryCode?> FindUnusedAsync(int userId, string selector);

        /// <summary>
        /// Marks the code used if it is still unused and still belongs to the account. One
        /// conditional update, so of any number of concurrent attempts exactly one returns true,
        /// and an attempt racing a regeneration finds the row gone and returns false.
        /// </summary>
        Task<bool> TryConsumeAsync(int codeId, int userId, DateTime usedAtUtc);

        /// <summary>
        /// Replaces the account's whole set with <paramref name="codes"/>, under the account's row
        /// lock so concurrent regenerations cannot leave two sets behind. Returns whether a set
        /// existed before.
        /// </summary>
        /// <remarks>
        /// <see cref="NoRetryAttribute"/> because the method already retries transient faults
        /// through the execution strategy, and a whole-method rerun after a commit whose
        /// acknowledgement was lost would report the replacement as a first generation.
        /// </remarks>
        [NoRetry]
        Task<bool> ReplaceSetAsync(int userId, IReadOnlyList<RecoveryCode> codes);
    }

    /// <param name="GeneratedAtUtc">When the current set was generated; null when there is none.</param>
    public sealed record RecoveryCodeStatus(int RemainingCount, DateTime? GeneratedAtUtc);
}
