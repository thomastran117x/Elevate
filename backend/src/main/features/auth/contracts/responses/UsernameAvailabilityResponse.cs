namespace backend.main.features.auth.contracts.responses;

/// <summary>
/// Result of a username availability probe.
/// </summary>
public sealed class UsernameAvailabilityResponse
{
    /// <summary>The normalised form the username was checked as, so the client can show what it evaluated.</summary>
    public required string Username
    {
        get; set;
    }

    /// <summary>
    /// True when nothing currently holds the name. Advisory only: the name is not reserved by
    /// asking, and signup can still fail with USERNAME_TAKEN if someone claims it first.
    /// </summary>
    public required bool Available
    {
        get; set;
    }
}
