using backend.main.application.environment;
using backend.main.seeders;

namespace backend.main.features.auth
{
    /// <summary>
    /// Dev/test-only policy that decides whether captcha and MFA gates may be
    /// bypassed for a seed account. A bypass is permitted only when ALL of:
    /// the opt-in flag is enabled, the environment is affirmatively non-production
    /// (see <see cref="BypassEnvironment"/>, which defaults to production when
    /// unset so this can never fail open in production), and the account's email
    /// is under <see cref="SeedCatalogConstants.SeedEmailDomain"/>.
    ///
    /// Trust boundary: the seed check is an email-suffix match, not a lookup against
    /// the accounts the seeder actually created. In a non-production environment with
    /// the flag enabled, anyone who registers an address under that domain gains the
    /// bypass. This is acceptable only because it is gated to dev/test with an explicit
    /// opt-in; do not repurpose this policy for production trust decisions.
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
    }
}
