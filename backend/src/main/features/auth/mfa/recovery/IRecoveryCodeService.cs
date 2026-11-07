namespace backend.main.features.auth.mfa.recovery
{
    public interface IRecoveryCodeService
    {
        Task<RecoveryCodeStatus> GetStatusAsync(int userId);

        /// <summary>Whether the account has a code left, so the method can be offered.</summary>
        Task<bool> HasUnusedAsync(int userId);

        /// <summary>Identifies the current set for the step-up factor state; null with no set.</summary>
        Task<DateTime?> GetSetGeneratedAtAsync(int userId);

        /// <summary>
        /// Replaces the account's codes with a new set and returns its plaintext, the only time
        /// it exists. The caller records the change; this method sends nothing.
        /// </summary>
        Task<RecoveryCodeSet> RegenerateAsync(int userId);

        /// <summary>
        /// Spends <paramref name="code"/> if it is an unused code of an enabled account, and
        /// returns the spend so a caller whose own step then fails can hand it back. Returns null
        /// for a wrong, used, or replaced code, or a disabled or missing account. Throws
        /// <see cref="shared.exceptions.http.BadRequestException"/> for input that cannot be a
        /// code, before any check is made.
        /// </summary>
        Task<RecoveryCodeSpend?> TryConsumeAsync(int userId, string code);

        /// <summary>
        /// Returns a code spent by <see cref="TryConsumeAsync"/> to the unused set, because what
        /// it was spent on (a session, a step-up proof) did not happen. Never throws: it runs on
        /// a failure path, and a release that fails leaves the code spent, which is the safe side.
        /// </summary>
        Task ReleaseAsync(int userId, RecoveryCodeSpend spend);
    }

    /// <summary>One spent code, identified well enough to undo exactly that spend.</summary>
    public sealed record RecoveryCodeSpend(int CodeId, DateTime UsedAtUtc);

    /// <param name="Codes">The plaintext codes, grouped for display.</param>
    /// <param name="Replaced">Whether an earlier set existed and is now invalid.</param>
    public sealed record RecoveryCodeSet(IReadOnlyList<string> Codes, DateTime GeneratedAtUtc, bool Replaced);
}
