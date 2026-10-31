namespace backend.main.features.auth.mfa.recovery
{
    public interface IRecoveryCodeService
    {
        Task<RecoveryCodeStatus> GetStatusAsync(int userId);

        /// <summary>Whether the account has a code left, so the method can be offered.</summary>
        Task<bool> HasUnusedAsync(int userId);

        /// <summary>
        /// Replaces the account's codes with a new set and returns its plaintext, the only time
        /// it exists. The caller records the change; this method sends nothing.
        /// </summary>
        Task<RecoveryCodeSet> RegenerateAsync(int userId);

        /// <summary>
        /// Spends <paramref name="code"/> if it is an unused code of an enabled account. Returns
        /// false for a wrong, used, or replaced code, or a disabled or missing account. Throws
        /// <see cref="shared.exceptions.http.BadRequestException"/> for input that cannot be a
        /// code, before any check is made.
        /// </summary>
        Task<bool> TryConsumeAsync(int userId, string code);
    }

    /// <param name="Codes">The plaintext codes, grouped for display.</param>
    /// <param name="Replaced">Whether an earlier set existed and is now invalid.</param>
    public sealed record RecoveryCodeSet(IReadOnlyList<string> Codes, DateTime GeneratedAtUtc, bool Replaced);
}
