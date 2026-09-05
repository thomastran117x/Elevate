namespace backend.main.features.profile.contracts;

public enum UsernameChangeStatus
{
    Changed,
    UserNotFound,
    Unchanged,
    CooldownActive,
    Unavailable,
}

/// <param name="Status">Outcome of the change attempt.</param>
/// <param name="User">The user row after the change, when one was loaded.</param>
/// <param name="AvailableAtUtc">When the cooldown lifts, for <see cref="UsernameChangeStatus.CooldownActive"/>.</param>
/// <param name="PreviousUsername">
/// The username released by a successful change, normalised. It is moved into the reservation
/// cooldown table rather than freed, so it stays unavailable and callers that track the username
/// namespace — the bloom filter in particular — must account for it.
/// </param>
public sealed record UsernameChangeRecord(
    UsernameChangeStatus Status,
    User? User = null,
    DateTime? AvailableAtUtc = null,
    string? PreviousUsername = null
);
