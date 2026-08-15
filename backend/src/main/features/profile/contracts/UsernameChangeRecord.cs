namespace backend.main.features.profile.contracts;

public enum UsernameChangeStatus
{
    Changed,
    UserNotFound,
    Unchanged,
    CooldownActive,
    Unavailable,
}

public sealed record UsernameChangeRecord(
    UsernameChangeStatus Status,
    User? User = null,
    DateTime? AvailableAtUtc = null
);
