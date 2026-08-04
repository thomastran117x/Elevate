namespace backend.main.features.events.waitlist
{
    /// <summary>
    /// Lifecycle of a single waitlist entry. Stored as a string (see AppDatabaseContext).
    /// </summary>
    public enum EventWaitlistEntryStatus
    {
        /// <summary>Queued and eligible for promotion.</summary>
        Waiting,

        /// <summary>Converted into an active registration, or closed because the user registered directly.</summary>
        Promoted,

        /// <summary>The user removed themselves from the queue.</summary>
        Left,

        /// <summary>An organizer removed the entry.</summary>
        Removed
    }
}
