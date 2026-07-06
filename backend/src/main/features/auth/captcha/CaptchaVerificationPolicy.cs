using backend.main.application.environment;

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
                ?? BypassEnvironment.ParseBool(config["CAPTCHA_ALLOW_BYPASS"]);
            _environmentName = BypassEnvironment.ResolveName(config);
        }

        public bool IsBypassEnabled => _allowBypass && IsNonProduction;

        public bool IsNonProduction => BypassEnvironment.IsNonProduction(_environmentName);

        public string EnvironmentName => _environmentName;
    }
}
