using backend.main.application.environment;
using backend.main.seeders;

namespace backend.main.features.auth
{
    /// <summary>
    /// Dev/test-only policy that decides whether captcha and MFA gates may be
    /// bypassed for a seed account. A bypass is permitted only when ALL of:
    /// the opt-in flag is enabled, the environment is affirmatively non-production
    /// (see <see cref="BypassEnvironment"/>, which defaults to production when
    /// unset so this can never fail open in production), and the supplied identity
    /// is either an email under <see cref="SeedCatalogConstants.SeedEmailDomain"/>
    /// or a username in <see cref="UserSeedCatalog"/>.
    ///
    /// Trust boundary: the email check is a suffix match, while the username check is
    /// restricted to the static seed catalog. This is acceptable only because both are
    /// gated to dev/test with an explicit opt-in; do not repurpose this policy for
    /// production trust decisions.
    /// </summary>
    public sealed class SeedAccountBypassPolicy
    {
        private readonly bool _allowBypass;
        private readonly string _environmentName;

        public SeedAccountBypassPolicy(IConfiguration config)
        {
            _allowBypass =
                config.GetValue<bool?>("Auth:SeedAccountBypass")
                ?? BypassEnvironment.ParseBool(config["AUTH_SEED_ACCOUNT_BYPASS"]);
            _environmentName = BypassEnvironment.ResolveName(config);
        }

        public bool IsNonProduction => BypassEnvironment.IsNonProduction(_environmentName);

        public string EnvironmentName => _environmentName;

        public bool IsBypassEnabledFor(string? email) =>
            _allowBypass
            && IsNonProduction
            && !string.IsNullOrWhiteSpace(email)
            && email.Trim().EndsWith(SeedCatalogConstants.SeedEmailDomain, StringComparison.OrdinalIgnoreCase);

        public bool IsBypassEnabledForUsername(string? username) =>
            _allowBypass
            && IsNonProduction
            && !string.IsNullOrWhiteSpace(username)
            && UserSeedCatalog.All.Any(user =>
                string.Equals(user.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
