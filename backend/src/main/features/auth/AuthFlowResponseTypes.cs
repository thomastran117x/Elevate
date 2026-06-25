namespace backend.main.features.auth
{
    public static class AuthFlowResponseTypes
    {
        public const string Authenticated = "authenticated";
        public const string RequiresStepUp = "requires_step_up";
        public const string RequiresRoleSelection = "requires_role_selection";
    }
}
