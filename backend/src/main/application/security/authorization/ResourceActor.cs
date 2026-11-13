using System.Security.Claims;

namespace backend.main.application.security.authorization;

/// <summary>
/// The caller a permission is evaluated for. Built from the request principal at the edge, or
/// from the user id and role a service was handed, so direct and transactional service entry
/// points are checked by the same rules as controllers.
/// </summary>
public sealed record ResourceActor(int? UserId, string? Role)
{
    public static readonly ResourceActor Anonymous = new(null, null);

    public bool IsAuthenticated => UserId.HasValue;

    public bool IsAdmin => string.Equals(
        AuthRoles.NormalizeStored(Role),
        AuthRoles.Admin,
        StringComparison.Ordinal);

    public static ResourceActor User(int userId, string? role) => new(userId, role);

    public static ResourceActor From(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return Anonymous;

        var payload = principal.GetUserPayload();
        return new ResourceActor(payload.Id, payload.Role);
    }

    /// <summary>The relationships every caller holds regardless of the resource.</summary>
    public ResourceRelationship BaseRelationships()
    {
        var held = ResourceRelationship.Anyone;
        if (IsAuthenticated)
            held |= ResourceRelationship.Authenticated;
        if (IsAuthenticated && IsAdmin)
            held |= ResourceRelationship.Admin;
        return held;
    }
}
