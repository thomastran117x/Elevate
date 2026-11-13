namespace backend.main.application.security.authorization;

/// <summary>How a refused caller is answered.</summary>
public enum PermissionDenial
{
    /// <summary><c>403 FORBIDDEN</c>.</summary>
    Forbid,

    /// <summary>
    /// <c>404 RESOURCE_NOT_FOUND</c>, indistinguishable from a resource that does not exist, for
    /// existence-sensitive resources such as private events and media assets.
    /// </summary>
    Hide,

    /// <summary><c>401 UNAUTHORIZED</c> for an anonymous caller, otherwise <c>403 FORBIDDEN</c>.</summary>
    ChallengeAnonymous,
}

/// <summary>
/// A named business permission. The catalog lives in <see cref="Permissions"/>; endpoints declare
/// one with <see cref="RequirePermissionAttribute"/> and services re-check it through
/// <see cref="IResourceAuthorizer"/>.
/// </summary>
/// <param name="Name">The stable name endpoints and the approved matrix refer to.</param>
/// <param name="Resource">The kind of resource the permission is checked against.</param>
/// <param name="Allowed">The relationships that always pass.</param>
/// <param name="AllowedWhenPublic">
/// Extra relationships that pass while the resource is public (a club that is not private).
/// </param>
/// <param name="Denial">How a refused caller is answered.</param>
/// <param name="Description">What the permission guards, for the generated matrix.</param>
/// <param name="DenialMessage">The default refusal message; call sites may override it.</param>
/// <param name="AnonymousDenialMessage">The default 401 message for <see cref="PermissionDenial.ChallengeAnonymous"/>.</param>
public sealed record Permission(
    string Name,
    ResourceKind Resource,
    ResourceRelationship Allowed,
    ResourceRelationship AllowedWhenPublic,
    PermissionDenial Denial,
    string Description,
    string DenialMessage = "Not allowed",
    string? AnonymousDenialMessage = null)
{
    public override string ToString() => Name;
}

/// <summary>Call-site wording for a refusal, so existing response messages stay unchanged.</summary>
public sealed record PermissionMessages(string? Forbidden = null, string? Anonymous = null);
