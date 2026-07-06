namespace backend.main.application.environment
{
    /// <summary>
    /// Shared, production-safe environment detection for dev/test-only bypass
    /// switches (captcha, seed-account MFA). Unlike the app's general
    /// <see cref="EnvironmentSetting"/> — which defaults an unset environment to
    /// "development" — this resolver defaults to <c>production</c> so a bypass can
    /// never fail open when the environment variable is missing (e.g. a production
    /// host relying on the framework's default environment).
    /// </summary>
    internal static class BypassEnvironment
    {
        public static string ResolveName(IConfiguration config) =>
            (
                config["ENVIRONMENT"]
                ?? config["ASPNETCORE_ENVIRONMENT"]
                ?? config["DOTNET_ENVIRONMENT"]
                ?? "production"
            ).Trim().ToLowerInvariant();

        public static bool IsNonProduction(string environmentName) =>
            environmentName is "development" or "dev" or "test";

        public static bool ParseBool(string? value) =>
            bool.TryParse(value, out var parsed) && parsed;
    }
}
