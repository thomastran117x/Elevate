namespace backend.main.application.security
{
    /// <summary>
    /// Names of the authorization policies registered by <see cref="JwtConfiguration.AddJwtAuth"/>.
    /// </summary>
    /// <remarks>
    /// Every endpoint without an authorization attribute falls back to requiring an authenticated
    /// user, so these named policies only narrow access further by global role. Resource ownership
    /// and club staff checks stay in the service layer.
    /// </remarks>
    public static class AuthorizationPolicies
    {
        public const string AdminOnly = "AdminOnly";
        public const string OrganizerOnly = "OrganizerOnly";
    }
}
