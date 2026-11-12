using System.Net;

using backend.main.application.environment;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.passkeys
{
    /// <summary>
    /// The WebAuthn relying party: the RP ID every passkey is scoped to, and the exact origins a
    /// ceremony's client data may name. When neither is configured, both are taken from
    /// <c>FRONTEND_URL</c>, which already has to match the frontend exactly.
    /// </summary>
    public sealed class PasskeyOptions
    {
        public const string SectionName = "Auth:Passkeys";

        public const int MinimumCeremonyTimeoutSeconds = 30;
        public const int MaximumCeremonyTimeoutSeconds = 600;
        public const int MaximumCredentialsLimit = 50;

        /// <summary>
        /// The registrable domain passkeys belong to, e.g. <c>example.com</c>. Changing it orphans
        /// every registered passkey, so users would need to register again.
        /// </summary>
        public string? RelyingPartyId
        {
            get; set;
        }

        /// <summary>The name authenticators show when a passkey is created.</summary>
        public string RelyingPartyName { get; set; } = "EventXperience";

        /// <summary>
        /// Every origin the frontend is served from, e.g. <c>https://app.example.com</c>. Each
        /// must be the RP ID or one of its subdomains.
        /// </summary>
        public string[] Origins { get; set; } = [];

        /// <summary>How long a ceremony's challenge stays usable, and the browser's timeout.</summary>
        public int CeremonyTimeoutSeconds { get; set; } = 300;

        /// <summary>The most passkeys one account may register.</summary>
        public int MaxCredentialsPerUser { get; set; } = 10;

        public TimeSpan CeremonyTimeout => TimeSpan.FromSeconds(CeremonyTimeoutSeconds);

        /// <summary>
        /// Fills an unset RP ID and origin list from the frontend address. Runs after binding, so
        /// explicit configuration always wins.
        /// </summary>
        internal void ApplyFrontendDefaults(string? frontendBaseUrl)
        {
            // An unset Compose variable binds as an empty entry, which means "not configured".
            Origins = Origins.Where(origin => !string.IsNullOrWhiteSpace(origin)).ToArray();

            if (!Uri.TryCreate(frontendBaseUrl, UriKind.Absolute, out var frontend))
                return;

            if (Origins.Length == 0)
                Origins = [frontend.GetLeftPart(UriPartial.Authority)];

            if (string.IsNullOrWhiteSpace(RelyingPartyId))
                RelyingPartyId = frontend.Host;
        }
    }

    public sealed class PasskeyOptionsValidator : IValidateOptions<PasskeyOptions>
    {
        private readonly bool _isNonProduction;

        public PasskeyOptionsValidator(IConfiguration config)
            : this(BypassEnvironment.IsNonProduction(BypassEnvironment.ResolveName(config)))
        {
        }

        internal PasskeyOptionsValidator(bool isNonProduction)
        {
            _isNonProduction = isNonProduction;
        }

        public ValidateOptionsResult Validate(string? name, PasskeyOptions options)
        {
            var failures = new List<string>();
            var rpId = options.RelyingPartyId?.Trim();

            if (string.IsNullOrWhiteSpace(rpId))
            {
                failures.Add("Auth:Passkeys:RelyingPartyId must be configured, or FRONTEND_URL must be an absolute URL.");
            }
            else if (!IsValidRelyingPartyId(rpId))
            {
                failures.Add(
                    "Auth:Passkeys:RelyingPartyId must be a lowercase domain without a scheme, port, or path, and not an IP address.");
            }
            else if (!_isNonProduction && IsLocalhost(rpId))
            {
                // The environment resolver defaults to production, so a host that forgot to name
                // its environment fails closed rather than scoping passkeys to localhost.
                failures.Add("Auth:Passkeys:RelyingPartyId cannot be localhost outside development and test.");
            }

            if (string.IsNullOrWhiteSpace(options.RelyingPartyName))
                failures.Add("Auth:Passkeys:RelyingPartyName must be configured.");

            if (options.Origins.Length == 0)
                failures.Add("Auth:Passkeys:Origins must list at least one origin.");

            foreach (var origin in options.Origins)
            {
                var failure = ValidateOrigin(origin, rpId);
                if (failure is not null)
                    failures.Add(failure);
            }

            if (options.CeremonyTimeoutSeconds is < PasskeyOptions.MinimumCeremonyTimeoutSeconds
                or > PasskeyOptions.MaximumCeremonyTimeoutSeconds)
            {
                failures.Add(
                    $"Auth:Passkeys:CeremonyTimeoutSeconds must be between {PasskeyOptions.MinimumCeremonyTimeoutSeconds} and {PasskeyOptions.MaximumCeremonyTimeoutSeconds}.");
            }

            if (options.MaxCredentialsPerUser is < 1 or > PasskeyOptions.MaximumCredentialsLimit)
            {
                failures.Add(
                    $"Auth:Passkeys:MaxCredentialsPerUser must be between 1 and {PasskeyOptions.MaximumCredentialsLimit}.");
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }

        private string? ValidateOrigin(string? origin, string? rpId)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("https" or "http")
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                return $"Auth:Passkeys:Origins entry '{origin}' must be a scheme, host, and optional port only.";
            }

            var localhost = IsLocalhost(uri.Host);
            if (uri.Scheme == "http" && !localhost)
                return $"Auth:Passkeys:Origins entry '{origin}' must use https; only localhost may use http.";

            if (localhost && !_isNonProduction)
                return $"Auth:Passkeys:Origins entry '{origin}' cannot be localhost outside development and test.";

            if (!string.IsNullOrWhiteSpace(rpId)
                && !uri.Host.Equals(rpId, StringComparison.OrdinalIgnoreCase)
                && !uri.Host.EndsWith("." + rpId, StringComparison.OrdinalIgnoreCase))
            {
                return $"Auth:Passkeys:Origins entry '{origin}' is not the RP ID '{rpId}' or one of its subdomains.";
            }

            return null;
        }

        /// <remarks>
        /// Browsers refuse an IP address as an RP ID, and a single label other than localhost is
        /// a public suffix or a typo. Neither check replaces the browser's own public-suffix test.
        /// </remarks>
        internal static bool IsValidRelyingPartyId(string rpId)
        {
            if (rpId != rpId.ToLowerInvariant() || IPAddress.TryParse(rpId, out _))
                return false;

            if (Uri.CheckHostName(rpId) != UriHostNameType.Dns)
                return false;

            return IsLocalhost(rpId) || (rpId.Contains('.') && !rpId.StartsWith('.') && !rpId.EndsWith('.'));
        }

        private static bool IsLocalhost(string host) =>
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }
}
