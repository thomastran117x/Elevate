namespace backend.main.features.events;

/// <summary>
/// A lifecycle move the current actor may make on an event, described richly enough that the
/// client can render both the button and its confirmation prompt without knowing the state
/// machine. Produced by <see cref="EventLifecyclePolicy.GetAvailableTransitions"/>.
/// </summary>
/// <param name="Key">Matches the endpoint segment and the <c>EventVersionActions</c> value.</param>
/// <param name="Target">The state the event lands in.</param>
/// <param name="Label">Button text, e.g. "Pause event".</param>
/// <param name="Title">Confirmation heading, e.g. "Pause this event?".</param>
/// <param name="IsReversible">Whether this move can be walked back by another ordinary transition.</param>
/// <param name="ReversibleNote">Reassurance shown in the prompt, or null when not reversible.</param>
/// <param name="IsDestructive">Drives danger styling and a more deliberate confirm on the client.</param>
/// <param name="Impacts">Concrete consequences, already resolved against this event's real numbers.</param>
/// <param name="BlockedReason">
/// Why this move cannot be made right now, or null when it can. Computed server-side so the
/// client never has to work out which readiness rules apply to which transition.
/// </param>
public sealed record EventLifecycleTransition(
    string Key,
    EventLifecycleState Target,
    string Label,
    string Title,
    bool IsReversible,
    string? ReversibleNote,
    bool IsDestructive,
    IReadOnlyList<string> Impacts,
    string? BlockedReason = null);
