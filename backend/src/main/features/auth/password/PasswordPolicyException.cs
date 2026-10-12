using backend.main.shared.exceptions.http;

namespace backend.main.features.auth.password
{
    /// <summary>
    /// A password rejected by <see cref="IPasswordPolicy"/>. Shaped like a model-validation failure
    /// (400 <c>VALIDATION_ERROR</c>, details keyed by field), so clients handle a rule the service
    /// layer enforces exactly like one a request attribute enforces.
    /// </summary>
    public sealed class PasswordPolicyException : AppException
    {
        public PasswordPolicyException(string field, string message)
            : base(
                "Validation failed.",
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                new Dictionary<string, string[]> { [field] = [message] })
        {
        }
    }
}
