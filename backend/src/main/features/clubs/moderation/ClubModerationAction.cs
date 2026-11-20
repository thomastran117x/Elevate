namespace backend.main.features.clubs.moderation;

/// <summary>
/// One moderation action in a club: a staff member or admin changed or removed content someone
/// else wrote. Written in the same unit of work as the change it records.
/// </summary>
public class ClubModerationAction
{
    public int Id
    {
        get; set;
    }
    public int ClubId
    {
        get; set;
    }
    public int ActorUserId
    {
        get; set;
    }

    /// <summary>The actor's standing when they acted: Owner, Manager, Volunteer or Admin.</summary>
    public string ActorClubRole { get; set; } = string.Empty;

    /// <summary>One of <see cref="ClubModerationActionTypes"/>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>One of <see cref="ClubModerationContentTypes"/>.</summary>
    public string ContentType { get; set; } = string.Empty;
    public int ContentId
    {
        get; set;
    }
    public int AuthorUserId
    {
        get; set;
    }

    /// <summary>The content's title or opening text as it was before the action.</summary>
    public string? ContentSnapshot
    {
        get; set;
    }
    public string? Reason
    {
        get; set;
    }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
