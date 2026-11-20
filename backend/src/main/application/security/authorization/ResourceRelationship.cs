namespace backend.main.application.security.authorization;

/// <summary>The kinds of resource a <see cref="Permission"/> can be checked against.</summary>
public enum ResourceKind
{
    Club,
    Event,
    EventSeries,
    Payment,
    MediaAsset,
    User,
    AuthoredContent,

    /// <summary>Content in a club (a post, discussion, reply, comment or review) with a known author.</summary>
    ClubContent,
}

/// <summary>
/// A caller's relationship to a resource. A <see cref="Permission"/> lists the relationships it
/// allows, so every override (including the global Admin role) is written down per permission
/// instead of being inferred from a role string inside a service.
/// </summary>
[Flags]
public enum ResourceRelationship
{
    None = 0,

    /// <summary>Every caller, signed in or not.</summary>
    Anyone = 1 << 0,

    /// <summary>Any signed-in caller.</summary>
    Authenticated = 1 << 1,

    /// <summary>A member of the club (a <c>FollowClub</c> row).</summary>
    Member = 1 << 2,

    /// <summary>Club staff with the Volunteer role.</summary>
    Volunteer = 1 << 3,

    /// <summary>Club staff with the Manager role.</summary>
    Manager = 1 << 4,

    /// <summary>The club's owner (<c>Club.UserId</c>).</summary>
    Owner = 1 << 5,

    /// <summary>The global Admin role.</summary>
    Admin = 1 << 6,

    /// <summary>The caller is the user the resource belongs to.</summary>
    Self = 1 << 7,

    /// <summary>The caller wrote the content.</summary>
    Author = 1 << 8,

    /// <summary>The caller uploaded the media asset.</summary>
    Uploader = 1 << 9,

    /// <summary>The caller holds an active registration for the event.</summary>
    Registrant = 1 << 10,

    /// <summary>The caller accepted an invitation to the event.</summary>
    Invitee = 1 << 11,

    /// <summary>The caller has a pending or succeeded payment for the event.</summary>
    Payer = 1 << 12,

    /// <summary>
    /// The caller's staff rank in the content's club (Owner, then Manager, then Volunteer) is
    /// above the author's. Anyone who is not staff of that club has no rank.
    /// </summary>
    SeniorStaff = 1 << 13,

    /// <summary>Owner, Manager, or Volunteer.</summary>
    ClubStaff = Volunteer | Manager | Owner,
}
