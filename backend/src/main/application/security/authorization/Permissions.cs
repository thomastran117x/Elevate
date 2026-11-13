using static backend.main.application.security.authorization.ResourceRelationship;

namespace backend.main.application.security.authorization;

/// <summary>Permission names, as constants so attributes can refer to them.</summary>
public static class PermissionNames
{
    public const string ClubRead = "club.read";
    public const string ClubParticipate = "club.participate";
    public const string ClubContribute = "club.contribute";
    public const string ClubStaff = "club.staff";
    public const string ClubManage = "club.manage";
    public const string ClubOwn = "club.own";
    public const string EventView = "event.view";
    public const string EventManage = "event.manage";
    public const string EventMedia = "event.media";
    public const string SeriesManage = "series.manage";
    public const string PaymentOwner = "payment.owner";
    public const string MediaView = "media.view";
    public const string UserSelfOrAdmin = "user.self-or-admin";
    public const string ContentAuthor = "content.author";
}

/// <summary>
/// The canonical permission catalog: every reusable resource decision the API makes. The approved
/// matrix (docs/AUTHORIZATION.md, checked by the Auth Contract Gate) maps each protected endpoint
/// to these permissions. Changing who a permission allows is a product decision; record it in the
/// matrix in the same change.
/// </summary>
public static class Permissions
{
    private const ResourceRelationship ClubManagers = Manager | Owner | Admin;
    private const ResourceRelationship ClubStaffOrAdmin = ClubStaff | Admin;
    private const ResourceRelationship MembersAndStaff = Member | ClubStaff | Admin;

    public static readonly Permission ClubRead = new(
        PermissionNames.ClubRead,
        ResourceKind.Club,
        Allowed: MembersAndStaff,
        AllowedWhenPublic: Anyone,
        PermissionDenial.ChallengeAnonymous,
        "Read a club's posts, discussions, comments and realtime feed. Public clubs are open to everyone.",
        DenialMessage: "You must be a member of this club.",
        AnonymousDenialMessage: "Authentication is required for a private club.");

    public static readonly Permission ClubParticipate = new(
        PermissionNames.ClubParticipate,
        ResourceKind.Club,
        Allowed: MembersAndStaff,
        AllowedWhenPublic: Authenticated,
        PermissionDenial.ChallengeAnonymous,
        "Reply, comment and react. Public clubs are open to any signed-in user.",
        DenialMessage: "You must be a member of this club.",
        AnonymousDenialMessage: "Authentication is required for a private club.");

    public static readonly Permission ClubContribute = new(
        PermissionNames.ClubContribute,
        ResourceKind.Club,
        Allowed: MembersAndStaff,
        AllowedWhenPublic: None,
        PermissionDenial.Forbid,
        "Start a discussion. Members and staff only, even in public clubs.",
        DenialMessage: "You must be a member of this club.");

    public static readonly Permission ClubStaffAccess = new(
        PermissionNames.ClubStaff,
        ResourceKind.Club,
        Allowed: ClubStaffOrAdmin,
        AllowedWhenPublic: None,
        PermissionDenial.Forbid,
        "Staff work: create, edit, pin and delete club posts.");

    public static readonly Permission ClubManage = new(
        PermissionNames.ClubManage,
        ResourceKind.Club,
        Allowed: ClubManagers,
        AllowedWhenPublic: None,
        PermissionDenial.Forbid,
        "Edit the club, its versions and rollbacks, member invitations and links, analytics, events and series.");

    public static readonly Permission ClubOwn = new(
        PermissionNames.ClubOwn,
        ResourceKind.Club,
        Allowed: Owner | Admin,
        AllowedWhenPublic: None,
        PermissionDenial.Forbid,
        "Delete the club, list, add and remove staff, staff invitations, and transfer ownership.");

    public static readonly Permission EventView = new(
        PermissionNames.EventView,
        ResourceKind.Event,
        Allowed: ClubStaffOrAdmin | Registrant | Invitee | Payer,
        AllowedWhenPublic: Anyone,
        PermissionDenial.Hide,
        "Read an event and act on it as a participant. Only published, paused and cancelled events are visible; "
            + "private ones only to the listed relationships.",
        DenialMessage: "Event not found");

    public static readonly Permission EventManage = new(
        PermissionNames.EventManage,
        ResourceKind.Event,
        Allowed: ClubManagers,
        AllowedWhenPublic: None,
        PermissionDenial.Forbid,
        "Manage an event through its club: edit, lifecycle, versions, analytics, waitlist, invitations.");

    public static readonly Permission EventMedia = new(
        PermissionNames.EventMedia,
        ResourceKind.Event,
        Allowed: ClubStaffOrAdmin,
        AllowedWhenPublic: None,
        PermissionDenial.Forbid,
        "Manage an event's images through its club.");

    public static readonly Permission SeriesManage = new(
        PermissionNames.SeriesManage,
        ResourceKind.EventSeries,
        Allowed: ClubManagers,
        AllowedWhenPublic: None,
        PermissionDenial.Forbid,
        "Manage a recurring series through its club.");

    public static readonly Permission PaymentOwner = new(
        PermissionNames.PaymentOwner,
        ResourceKind.Payment,
        Allowed: Self,
        AllowedWhenPublic: None,
        PermissionDenial.Forbid,
        "Read or refund one's own payment.");

    public static readonly Permission MediaView = new(
        PermissionNames.MediaView,
        ResourceKind.MediaAsset,
        Allowed: Uploader | ClubManagers,
        AllowedWhenPublic: None,
        PermissionDenial.Hide,
        "Read a media asset's processing status.",
        DenialMessage: "Media asset not found.");

    public static readonly Permission UserSelfOrAdmin = new(
        PermissionNames.UserSelfOrAdmin,
        ResourceKind.User,
        Allowed: Self | Admin,
        AllowedWhenPublic: None,
        PermissionDenial.Forbid,
        "Read another user's private lists.");

    public static readonly Permission ContentAuthor = new(
        PermissionNames.ContentAuthor,
        ResourceKind.AuthoredContent,
        Allowed: Author,
        AllowedWhenPublic: None,
        PermissionDenial.Forbid,
        "Edit or delete one's own discussion, reply, comment or review.");

    public static readonly IReadOnlyList<Permission> All =
    [
        ClubRead,
        ClubParticipate,
        ClubContribute,
        ClubStaffAccess,
        ClubManage,
        ClubOwn,
        EventView,
        EventManage,
        EventMedia,
        SeriesManage,
        PaymentOwner,
        MediaView,
        UserSelfOrAdmin,
        ContentAuthor,
    ];

    private static readonly Dictionary<string, Permission> ByName =
        All.ToDictionary(permission => permission.Name, StringComparer.Ordinal);

    public static Permission Get(string name) =>
        ByName.TryGetValue(name, out var permission)
            ? permission
            : throw new InvalidOperationException($"Unknown permission '{name}'.");

    public static bool TryGet(string name, out Permission permission) =>
        ByName.TryGetValue(name, out permission!);
}
