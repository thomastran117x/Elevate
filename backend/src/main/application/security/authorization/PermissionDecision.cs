namespace backend.main.application.security.authorization;

/// <remarks><see cref="Denied"/> is the default value, so an uninitialized decision fails closed.</remarks>
public enum PermissionOutcome
{
    Denied,

    Allowed,

    /// <summary>The resource does not exist. Callers report it with their usual not-found error.</summary>
    ResourceNotFound,
}

/// <summary>The result of evaluating one permission for one caller and resource.</summary>
/// <param name="Outcome">Whether the caller passed.</param>
/// <param name="Held">The relationships the caller was found to hold, for diagnostics and tests.</param>
public readonly record struct PermissionDecision(PermissionOutcome Outcome, ResourceRelationship Held)
{
    public static readonly PermissionDecision NotFound = new(PermissionOutcome.ResourceNotFound, ResourceRelationship.None);

    public bool IsAllowed => Outcome == PermissionOutcome.Allowed;

    /// <summary>Applies a permission's relationship rules to what the caller holds.</summary>
    public static PermissionDecision Decide(Permission permission, ResourceRelationship held, bool resourceIsPublic)
    {
        var allowed = (held & permission.Allowed) != 0
            || (resourceIsPublic && (held & permission.AllowedWhenPublic) != 0);
        return new PermissionDecision(allowed ? PermissionOutcome.Allowed : PermissionOutcome.Denied, held);
    }

    public static PermissionDecision Of(bool allowed, ResourceRelationship held) =>
        new(allowed ? PermissionOutcome.Allowed : PermissionOutcome.Denied, held);
}
