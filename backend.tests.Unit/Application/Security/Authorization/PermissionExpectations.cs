using backend.main.application.security.authorization;
using backend.tests.Unit.Support;

using static backend.tests.Unit.Support.AuthorizationTestWorld;

namespace backend.tests.Unit.Application.Security.Authorization;

/// <summary>What a caller is answered with.</summary>
public enum Expect
{
    Allow,

    /// <summary>403 FORBIDDEN.</summary>
    Forbid,

    /// <summary>404, indistinguishable from a missing resource.</summary>
    Hide,

    /// <summary>401 UNAUTHORIZED for an anonymous caller.</summary>
    Challenge,

    /// <summary>The resource does not exist.</summary>
    NotFound,
}

/// <summary>
/// The approved permission matrix, written out by hand: for every catalog permission, every
/// <see cref="Caller"/> on every representative resource. It is deliberately independent of the
/// rules in <see cref="Permissions"/>, so a change to who a permission allows fails here until the
/// matrix is updated with it. docs/AUTHORIZATION.md is generated from this table.
/// </summary>
public static class PermissionExpectations
{
    public sealed record Row(string Permission, string Resource, Caller Caller, Expect Expected);

    /// <summary>The representative resources, by label.</summary>
    public static readonly IReadOnlyDictionary<string, IAuthorizationResource> Resources =
        new Dictionary<string, IAuthorizationResource>(StringComparer.Ordinal)
        {
            ["public club"] = new ClubResource(PublicClubId),
            ["private club"] = new ClubResource(PrivateClubId),
            ["missing club"] = new ClubResource(MissingId),
            ["public event"] = new EventResource(PublicEventId),
            ["private event"] = new EventResource(PrivateEventId),
            ["draft event"] = new EventResource(DraftEventId),
            ["event in private club"] = new EventResource(PrivateClubEventId),
            ["missing event"] = new EventResource(MissingId),
            ["series"] = new EventSeriesResource(SeriesId),
            ["missing series"] = new EventSeriesResource(MissingId),
            ["payer's payment"] = new PaymentResource(OwnPaymentId),
            ["missing payment"] = new PaymentResource(MissingId),
            ["volunteer's club upload"] = new MediaAssetResource(ClubAssetId),
            ["member's clubless upload"] = new MediaAssetResource(LooseAssetId),
            ["missing asset"] = new MediaAssetResource(Guid.Empty),
            ["member's account"] = new UserResource(UserId(Caller.Member)),
            ["member's content"] = new AuthoredContentResource("Reply", 1, UserId(Caller.Member)),
            ["member's comment"] = new ClubContentResource(PublicClubId, "Comment", 1, UserId(Caller.Member)),
            ["member's comment in private club"] = new ClubContentResource(PrivateClubId, "Comment", 2, UserId(Caller.Member)),
            ["volunteer's comment"] = new ClubContentResource(PublicClubId, "Comment", 3, UserId(Caller.Volunteer)),
            ["manager's comment"] = new ClubContentResource(PublicClubId, "Comment", 4, UserId(Caller.Manager)),
            ["owner's comment"] = new ClubContentResource(PublicClubId, "Comment", 5, UserId(Caller.Owner)),
            ["volunteer's post"] = new ClubContentResource(PublicClubId, "Post", 6, UserId(Caller.Volunteer)),
            ["owner's post"] = new ClubContentResource(PublicClubId, "Post", 7, UserId(Caller.Owner)),
            ["content in missing club"] = new ClubContentResource(MissingId, "Comment", 8, UserId(Caller.Member)),
        };

    private static readonly Caller[] Staff = [Caller.Volunteer, Caller.Manager, Caller.Owner, Caller.Admin];
    private static readonly Caller[] Managers = [Caller.Manager, Caller.Owner, Caller.Admin];
    private static readonly Caller[] MembersAndStaff = [Caller.Member, .. Staff];

    public static readonly IReadOnlyList<Row> Rows =
    [
        // Club reads and participation follow the club's privacy.
        .. Grid(PermissionNames.ClubRead, "public club", Expect.Allow),
        .. Grid(PermissionNames.ClubRead, "private club", Expect.Forbid,
            allow: MembersAndStaff, (Caller.Anonymous, Expect.Challenge)),
        .. Grid(PermissionNames.ClubRead, "missing club", Expect.NotFound),

        .. Grid(PermissionNames.ClubParticipate, "public club", Expect.Allow,
            allow: [], (Caller.Anonymous, Expect.Challenge)),
        .. Grid(PermissionNames.ClubParticipate, "private club", Expect.Forbid,
            allow: MembersAndStaff, (Caller.Anonymous, Expect.Challenge)),
        .. Grid(PermissionNames.ClubParticipate, "missing club", Expect.NotFound),

        // Starting a discussion needs membership even in a public club.
        .. Grid(PermissionNames.ClubContribute, "public club", Expect.Forbid, allow: MembersAndStaff),
        .. Grid(PermissionNames.ClubContribute, "private club", Expect.Forbid, allow: MembersAndStaff),
        .. Grid(PermissionNames.ClubContribute, "missing club", Expect.NotFound),

        .. Grid(PermissionNames.ClubStaff, "public club", Expect.Forbid, allow: Staff),
        .. Grid(PermissionNames.ClubStaff, "private club", Expect.Forbid, allow: Staff),
        .. Grid(PermissionNames.ClubStaff, "missing club", Expect.NotFound),

        .. Grid(PermissionNames.ClubManage, "public club", Expect.Forbid, allow: Managers),
        .. Grid(PermissionNames.ClubManage, "private club", Expect.Forbid, allow: Managers),
        .. Grid(PermissionNames.ClubManage, "missing club", Expect.NotFound),

        // Admin holds the owner's powers explicitly (#331); managers do not.
        .. Grid(PermissionNames.ClubOwn, "public club", Expect.Forbid, allow: [Caller.Owner, Caller.Admin]),
        .. Grid(PermissionNames.ClubOwn, "private club", Expect.Forbid, allow: [Caller.Owner, Caller.Admin]),
        .. Grid(PermissionNames.ClubOwn, "missing club", Expect.NotFound),

        // Event visibility: denials are hidden as 404.
        .. Grid(PermissionNames.EventView, "public event", Expect.Allow),
        .. Grid(PermissionNames.EventView, "private event", Expect.Hide,
            allow: [.. Staff, Caller.Registrant, Caller.Invitee, Caller.Payer]),
        .. Grid(PermissionNames.EventView, "draft event", Expect.Hide),
        // Known deviation (#387): an event does not inherit its club's privacy.
        .. Grid(PermissionNames.EventView, "event in private club", Expect.Allow),
        .. Grid(PermissionNames.EventView, "missing event", Expect.NotFound),

        .. Grid(PermissionNames.EventManage, "public event", Expect.Forbid, allow: Managers),
        .. Grid(PermissionNames.EventManage, "private event", Expect.Forbid, allow: Managers),
        .. Grid(PermissionNames.EventManage, "missing event", Expect.NotFound),

        .. Grid(PermissionNames.EventMedia, "public event", Expect.Forbid, allow: Staff),
        .. Grid(PermissionNames.EventMedia, "missing event", Expect.NotFound),

        .. Grid(PermissionNames.SeriesManage, "series", Expect.Forbid, allow: Managers),
        .. Grid(PermissionNames.SeriesManage, "missing series", Expect.NotFound),

        // Known deviation: no admin read or refund of another user's payment.
        .. Grid(PermissionNames.PaymentOwner, "payer's payment", Expect.Forbid, allow: [Caller.Payer]),
        .. Grid(PermissionNames.PaymentOwner, "missing payment", Expect.NotFound),

        .. Grid(PermissionNames.MediaView, "volunteer's club upload", Expect.Hide, allow: [Caller.Volunteer, .. Managers]),
        .. Grid(PermissionNames.MediaView, "member's clubless upload", Expect.Hide, allow: [Caller.Member]),
        .. Grid(PermissionNames.MediaView, "missing asset", Expect.NotFound),

        .. Grid(PermissionNames.UserSelfOrAdmin, "member's account", Expect.Forbid, allow: [Caller.Member, Caller.Admin]),

        // Editing stays with the author: moderators remove content, they do not rewrite it.
        .. Grid(PermissionNames.ContentAuthor, "member's content", Expect.Forbid, allow: [Caller.Member]),

        // Posts (#402): the author (who must also still hold club.staff), managers, the owner and
        // admins. A volunteer cannot change a more senior author's post.
        .. Grid(PermissionNames.PostManage, "volunteer's post", Expect.Forbid, allow: Staff),
        .. Grid(PermissionNames.PostManage, "owner's post", Expect.Forbid, allow: Managers),
        .. Grid(PermissionNames.PostManage, "content in missing club", Expect.NotFound),

        // Removal (#402): the author, staff of a higher rank in that club, and admins. Another
        // club's manager has no rank here.
        .. Grid(PermissionNames.ContentRemove, "member's comment", Expect.Forbid, allow: MembersAndStaff),
        .. Grid(PermissionNames.ContentRemove, "member's comment in private club", Expect.Forbid, allow: MembersAndStaff),
        .. Grid(PermissionNames.ContentRemove, "volunteer's comment", Expect.Forbid, allow: Staff),
        .. Grid(PermissionNames.ContentRemove, "manager's comment", Expect.Forbid, allow: Managers),
        .. Grid(PermissionNames.ContentRemove, "owner's comment", Expect.Forbid, allow: [Caller.Owner, Caller.Admin]),
        .. Grid(PermissionNames.ContentRemove, "content in missing club", Expect.NotFound),
    ];

    private static IEnumerable<Row> Grid(
        string permission,
        string resource,
        Expect otherwise,
        Caller[]? allow = null,
        params (Caller Caller, Expect Expected)[] exceptions)
    {
        foreach (var caller in Enum.GetValues<Caller>())
        {
            var expected = otherwise;
            if (allow?.Contains(caller) == true)
                expected = Expect.Allow;
            foreach (var exception in exceptions.Where(entry => entry.Caller == caller))
                expected = exception.Expected;

            yield return new Row(permission, resource, caller, expected);
        }
    }
}
