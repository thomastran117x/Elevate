namespace backend.main.features.events.contracts.responses
{
    /// <summary>
    /// Wire shape of <see cref="EventLifecycleTransition"/>. The client renders its lifecycle
    /// buttons and confirmation prompts from these, so it never duplicates the state machine.
    /// </summary>
    public sealed class EventLifecycleTransitionResponse
    {
        /// <summary>Endpoint segment for the action, e.g. "pause".</summary>
        public string Key { get; set; } = string.Empty;

        public EventLifecycleState Target
        {
            get; set;
        }

        public string Label { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public bool IsReversible
        {
            get; set;
        }

        public string? ReversibleNote
        {
            get; set;
        }

        public bool IsDestructive
        {
            get; set;
        }

        public List<string> Impacts { get; set; } = new();

        /// <summary>Why this move cannot be made right now, or null when it can.</summary>
        public string? BlockedReason
        {
            get; set;
        }
    }
}
