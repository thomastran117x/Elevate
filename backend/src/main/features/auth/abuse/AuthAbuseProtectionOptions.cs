using Microsoft.Extensions.Options;

namespace backend.main.features.auth.abuse;

public sealed class AuthAbuseProtectionOptions
{
    public const string SectionName = "Auth:AbuseProtection";

    public string KeyPrefix { get; set; } = "auth:abuse:v1";

    public int SharedIpPermitLimit { get; set; } = 10;

    public TimeSpan SharedIpWindow { get; set; } = TimeSpan.FromMinutes(5);

    public int LoginAccountFailureLimit { get; set; } = 10;

    public TimeSpan LoginAccountFailureWindow { get; set; } = TimeSpan.FromMinutes(15);

    public int RecoveryTargetPermitLimit { get; set; } = 3;

    public TimeSpan RecoveryTargetWindow { get; set; } = TimeSpan.FromHours(1);

    public int VerificationTargetPermitLimit { get; set; } = 5;

    public TimeSpan VerificationTargetWindow { get; set; } = TimeSpan.FromMinutes(15);

    public int OAuthCompletionTargetPermitLimit { get; set; } = 5;

    public TimeSpan OAuthCompletionTargetWindow { get; set; } = TimeSpan.FromMinutes(15);

    public int MfaDeliveryTargetPermitLimit { get; set; } = 5;

    public TimeSpan MfaDeliveryTargetWindow { get; set; } = TimeSpan.FromMinutes(15);

    public int EmailAvailabilityIpPermitLimit { get; set; } = 15;

    public TimeSpan EmailAvailabilityIpWindow { get; set; } = TimeSpan.FromMinutes(1);

    public int EmailAvailabilityTargetPermitLimit { get; set; } = 5;

    public TimeSpan EmailAvailabilityTargetWindow { get; set; } = TimeSpan.FromMinutes(15);

    public int CaptchaFailureThreshold { get; set; } = 3;

    public TimeSpan DelayBase { get; set; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan DelayMaximum { get; set; } = TimeSpan.FromSeconds(8);
}

public sealed class AuthAbuseProtectionOptionsValidator
    : IValidateOptions<AuthAbuseProtectionOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthAbuseProtectionOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.KeyPrefix))
            failures.Add("Auth:AbuseProtection:KeyPrefix must not be blank.");

        ValidateBudget(
            failures,
            "SharedIp",
            options.SharedIpPermitLimit,
            options.SharedIpWindow);
        ValidateBudget(
            failures,
            "LoginAccountFailure",
            options.LoginAccountFailureLimit,
            options.LoginAccountFailureWindow);
        ValidateBudget(
            failures,
            "RecoveryTarget",
            options.RecoveryTargetPermitLimit,
            options.RecoveryTargetWindow);
        ValidateBudget(
            failures,
            "VerificationTarget",
            options.VerificationTargetPermitLimit,
            options.VerificationTargetWindow);
        ValidateBudget(
            failures,
            "OAuthCompletionTarget",
            options.OAuthCompletionTargetPermitLimit,
            options.OAuthCompletionTargetWindow);
        ValidateBudget(
            failures,
            "MfaDeliveryTarget",
            options.MfaDeliveryTargetPermitLimit,
            options.MfaDeliveryTargetWindow);
        ValidateBudget(
            failures,
            "EmailAvailabilityIp",
            options.EmailAvailabilityIpPermitLimit,
            options.EmailAvailabilityIpWindow);
        ValidateBudget(
            failures,
            "EmailAvailabilityTarget",
            options.EmailAvailabilityTargetPermitLimit,
            options.EmailAvailabilityTargetWindow);

        if (options.CaptchaFailureThreshold <= 0)
            failures.Add("Auth:AbuseProtection:CaptchaFailureThreshold must be positive.");
        else if (options.CaptchaFailureThreshold > options.LoginAccountFailureLimit)
            failures.Add(
                "Auth:AbuseProtection:CaptchaFailureThreshold must not exceed LoginAccountFailureLimit."
            );

        if (options.DelayBase <= TimeSpan.Zero)
            failures.Add("Auth:AbuseProtection:DelayBase must be positive.");
        if (options.DelayMaximum < options.DelayBase)
            failures.Add("Auth:AbuseProtection:DelayMaximum must be at least DelayBase.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateBudget(
        ICollection<string> failures,
        string name,
        int limit,
        TimeSpan window)
    {
        if (limit <= 0)
            failures.Add($"Auth:AbuseProtection:{name}PermitLimit must be positive.");
        if (window <= TimeSpan.Zero)
            failures.Add($"Auth:AbuseProtection:{name}Window must be positive.");
    }
}
