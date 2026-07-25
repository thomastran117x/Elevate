namespace backend.main.features.events.waitlist
{
    /// <summary>A user who was moved off the waitlist into an active registration.</summary>
    public sealed record WaitlistPromotion(int EntryId, int UserId, string Email, string? RecipientName);

    /// <summary>
    /// Converts waitlist entries into registrations when seats free up.
    ///
    /// Deliberately depends on NO feature services (in particular not IEventsService), because
    /// it is called *from* EventRegistrationService and EventsService. Visibility checks go
    /// through IEventAccessChecker instead.
    /// </summary>
    public interface IEventWaitlistPromoter
    {
        /// <summary>
        /// Promotes as many waiting users as there are free seats, participating in a
        /// transaction the caller already owns.
        ///
        /// PRECONDITIONS — the caller MUST:
        ///   1. hold the Redis lock <see cref="registration.EventRegistrationCacheKeys.Lock"/>;
        ///   2. have an open IsolationLevel.Serializable transaction on the SAME
        ///      AppDatabaseContext instance;
        ///   3. have already flushed its own mutations via SaveChangesAsync, so the
        ///      active-registration count read here is accurate.
        ///
        /// This method never acquires a lock, commits, publishes email, or invalidates cache —
        /// the caller owns all four. Call PublishPromotionEmailsAsync and
        /// InvalidateForPromotedAsync after committing.
        /// </summary>
        Task<IReadOnlyList<WaitlistPromotion>> PromoteWithinTransactionAsync(int eventId, DateTime nowUtc);

        /// <summary>
        /// Self-contained promotion: acquires the lock, opens its own Serializable transaction,
        /// commits, invalidates cache and publishes emails. Never throws — logs and returns 0
        /// on failure, so callers on a non-waitlist hot path cannot be broken by it.
        /// </summary>
        Task<int> PromoteStandaloneAsync(int eventId);

        /// <summary>Post-commit email fan-out. Swallows and logs publisher failures.</summary>
        Task PublishPromotionEmailsAsync(
            IReadOnlyList<WaitlistPromotion> promotions,
            int eventId,
            string? eventName,
            DateTime? startsAtUtc);

        /// <summary>
        /// Invalidates the promoted users' caches. Easy to miss: each promoted user is a
        /// *different* user from the one whose action freed the seat.
        /// </summary>
        Task InvalidateForPromotedAsync(IReadOnlyList<WaitlistPromotion> promotions, int eventId);
    }
}
