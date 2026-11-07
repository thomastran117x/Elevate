using backend.main.application.environment;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.mfa.recovery
{
    public sealed class RecoveryCodeOptions
    {
        public const string SectionName = "Auth:RecoveryCodes";

        /// <summary>The smallest pepper accepted, in bytes.</summary>
        public const int MinimumPepperBytes = 32;

        /// <summary>
        /// The pepper development and test hosts fall back to. It is public, so it adds nothing
        /// there; startup refuses it anywhere else.
        /// </summary>
        internal const string DevelopmentPepper = "ZGV2ZWxvcG1lbnRfb25seV9tZmFfcmVjb3ZlcnlfY29kZV9wZXBwZXI=";

        /// <summary>
        /// Base64 secret mixed into every recovery-code hash and selector, kept outside the
        /// database so a leaked table alone cannot be used to check guesses. Required outside
        /// development and test. Changing it invalidates every stored code.
        /// </summary>
        public string? Pepper
        {
            get; set;
        }

        internal byte[] PepperBytes() =>
            Convert.FromBase64String(string.IsNullOrWhiteSpace(Pepper) ? DevelopmentPepper : Pepper);
    }

    public sealed class RecoveryCodeOptionsValidator : IValidateOptions<RecoveryCodeOptions>
    {
        private readonly bool _isNonProduction;

        public RecoveryCodeOptionsValidator(IConfiguration config)
            : this(BypassEnvironment.IsNonProduction(BypassEnvironment.ResolveName(config)))
        {
        }

        internal RecoveryCodeOptionsValidator(bool isNonProduction)
        {
            _isNonProduction = isNonProduction;
        }

        public ValidateOptionsResult Validate(string? name, RecoveryCodeOptions options)
        {
            var pepper = options.Pepper;
            if (string.IsNullOrWhiteSpace(pepper) || pepper == RecoveryCodeOptions.DevelopmentPepper)
            {
                // The environment resolver defaults to production, so a host that forgot to name
                // its environment fails closed here rather than peppering with a published value.
                return _isNonProduction
                    ? ValidateOptionsResult.Success
                    : ValidateOptionsResult.Fail(
                        "Auth:RecoveryCodes:Pepper must be configured outside development and test."
                    );
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(pepper);
            }
            catch (FormatException)
            {
                return ValidateOptionsResult.Fail("Auth:RecoveryCodes:Pepper is not valid base64.");
            }

            return bytes.Length >= RecoveryCodeOptions.MinimumPepperBytes
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    $"Auth:RecoveryCodes:Pepper must decode to at least {RecoveryCodeOptions.MinimumPepperBytes} bytes."
                );
        }
    }
}
