namespace backend.main.features.auth.captcha
{
    internal sealed class CaptchaVerificationPolicy
    {
        private readonly bool _allowBypass;
        private readonly string _environmentName;

        public CaptchaVerificationPolicy(IConfiguration config)
        {
            _allowBypass =
                config.GetValue<bool?>("Captcha:AllowBypass")
                ?? ParseBool(config["CAPTCHA_ALLOW_BYPASS"]);
            _environmentName = (
                config["ENVIRONMENT"]
                ?? config["ASPNETCORE_ENVIRONMENT"]
                ?? config["DOTNET_ENVIRONMENT"]
                ?? "development"
            ).Trim().ToLowerInvariant();
        }

        public bool IsBypassEnabled => _allowBypass && IsNonProduction;

        public bool IsNonProduction => _environmentName is "development" or "dev" or "test";

        public string EnvironmentName => _environmentName;

        private static bool ParseBool(string? value)
        {
            return bool.TryParse(value, out var parsed) && parsed;
        }
    }
}
