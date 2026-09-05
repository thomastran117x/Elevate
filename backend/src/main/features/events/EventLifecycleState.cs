namespace backend.main.features.events
{
    /// <summary>
    /// The organizer-controlled state of an event.
    /// <para>
    /// These values are persisted as integers and, because no global
    /// <c>JsonStringEnumConverter</c> is registered, they also travel over the wire as integers.
    /// The Angular client decodes them positionally against <c>ALL_LIFECYCLE_STATES</c>, so new
    /// members must only ever be <em>appended</em> — renumbering silently remaps every stored row.
    /// </para>
    /// </summary>
    public enum EventLifecycleState
    {
        Draft = 0,
        Published = 1,
        Cancelled = 2,
        Archived = 3,

        /// <summary>
        /// Temporarily withdrawn from public listings with registration closed, but with existing
        /// registrations preserved and the event still editable. Reversible via <c>resume</c>.
        /// </summary>
        Paused = 4
    }
}
