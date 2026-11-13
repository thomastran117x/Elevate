namespace backend.main.application.security
{
    /// <summary>
    /// Names of the authorization policies registered by <see cref="JwtConfiguration.AddJwtAuth"/>.
    /// </summary>
    /// <remarks>
    /// Every endpoint without an authorization attribute falls back to requiring an authenticated
    /// user, so these named policies only narrow access further by global role. Resource ownership,
    /// club staff and membership decisions are catalog permissions in
    /// <see cref="authorization.Permissions"/>, declared with
    /// <see cref="authorization.RequirePermissionAttribute"/> and re-checked by the services.
    /// </remarks>
    public static class AuthorizationPolicies
    {
        public const string AdminOnly = "AdminOnly";
        public const string OrganizerOnly = "OrganizerOnly";
    }
}
