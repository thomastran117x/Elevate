using Microsoft.Extensions.Options;

namespace backend.main.features.auth.mfa.session
{
    /// <summary>
    /// Settings for the in-session step-up proof that <c>[RequireMfa]</c> routes demand.
    /// </summary>
    public sealed class StepUpOptions
    {
        public const string SectionName = "Auth:StepUp";

        /// <summary>
        /// The longest a step-up proof may cover, measured from the moment of verification.
        /// The window never slides, so using a gated route does not extend it.
        /// </summary>
        public const int MaxProofLifetimeMinutes = 10;

        public int ProofLifetimeMinutes { get; set; } = MaxProofLifetimeMinutes;

        public TimeSpan ProofLifetime => TimeSpan.FromMinutes(ProofLifetimeMinutes);
    }

    public sealed class StepUpOptionsValidator : IValidateOptions<StepUpOptions>
    {
        public ValidateOptionsResult Validate(string? name, StepUpOptions options)
        {
            if (options.ProofLifetimeMinutes is < 1 or > StepUpOptions.MaxProofLifetimeMinutes)
            {
                return ValidateOptionsResult.Fail(
                    $"Auth:StepUp:ProofLifetimeMinutes must be between 1 and {StepUpOptions.MaxProofLifetimeMinutes}."
                );
            }

            return ValidateOptionsResult.Success;
        }
    }
}
