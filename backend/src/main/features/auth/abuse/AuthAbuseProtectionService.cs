using System.Net;
using System.Security.Cryptography;
using System.Text;

using backend.main.features.profile;
using backend.main.shared.exceptions.http;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.abuse;

public enum AuthAbuseFlow
{
    General,
    Recovery,
    Verification,
    OAuthCompletion,
    MfaDelivery,
    EmailAvailability,
}

public enum AuthAbuseTargetKind
{
    SourceIp,
    Username,
    Email,
    AccountId,
    Challenge,
}

public readonly record struct LoginAbuseState(
    long FailureCount,
    bool CaptchaRequired,
    bool Throttled,
    bool StoreAvailable,
    TimeSpan? RetryAfter);

public interface IAuthAbuseProtectionService
{
    Task EnsureSourceAllowedAsync(
        AuthAbuseFlow flow,
        string sourceIp,
        CancellationToken cancellationToken = default);

    Task EnsureTargetAllowedAsync(
        AuthAbuseFlow flow,
        AuthAbuseTargetKind kind,
        string target,
        CancellationToken cancellationToken = default);

    Task<LoginAbuseState> GetLoginStateAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task<LoginAbuseState> EnsureLoginAllowedAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task<LoginAbuseState> RecordLoginFailureAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task DelayFailedLoginAsync(
        long failureCount,
        CancellationToken cancellationToken = default);

    Task ResetLoginFailuresAsync(
        string username,
        CancellationToken cancellationToken = default);
}

public sealed class AuthAbuseProtectionService : IAuthAbuseProtectionService
{
    private static readonly EventId RedisUnavailableEvent = new(32001, "AuthAbuseRedisUnavailable");
    private static readonly EventId RedisRecoveredEvent = new(32002, "AuthAbuseRedisRecovered");
    private static readonly EventId ThrottledEvent = new(32003, "AuthAbuseThrottled");
    private static readonly EventId LoginSuccessEvent = new(32004, "AuthLoginFailureStateReset");

    private readonly IAuthAbuseProtectionStore _store;
    private readonly AuthAbuseProtectionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthAbuseProtectionService> _logger;
    private int _redisDegraded;

    public AuthAbuseProtectionService(
        IAuthAbuseProtectionStore store,
        IOptions<AuthAbuseProtectionOptions> options,
        TimeProvider timeProvider,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthAbuseProtectionService> logger)
    {
        _store = store;
        _options = options.Value;
        _timeProvider = timeProvider;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task EnsureSourceAllowedAsync(
        AuthAbuseFlow flow,
        string sourceIp,
        CancellationToken cancellationToken = default)
    {
        var availabilityFlow = flow == AuthAbuseFlow.EmailAvailability;
        var limit = availabilityFlow
            ? _options.EmailAvailabilityIpPermitLimit
            : _options.SharedIpPermitLimit;
        var window = availabilityFlow
            ? _options.EmailAvailabilityIpWindow
            : _options.SharedIpWindow;
        var key = BuildKey(
            availabilityFlow ? "availability" : "shared",
            "ip",
            AuthAbuseTargetKind.SourceIp,
            NormalizeIp(sourceIp));
        var result = await _store.ReserveAsync(key, limit, window, cancellationToken);
        HandleDecision(result, flow, "source-ip");
    }

    public async Task EnsureTargetAllowedAsync(
        AuthAbuseFlow flow,
        AuthAbuseTargetKind kind,
        string target,
        CancellationToken cancellationToken = default)
    {
        var (limit, window) = TargetBudget(flow);
        var key = BuildKey(FlowName(flow), "target", kind, NormalizeTarget(kind, target));
        var result = await _store.ReserveAsync(key, limit, window, cancellationToken);
        HandleDecision(result, flow, "target");
    }

    public async Task<LoginAbuseState> GetLoginStateAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var key = LoginFailureKey(username);
        var result = await _store.ReadAsync(
            key,
            _options.LoginAccountFailureLimit,
            cancellationToken);
        ObserveStore(result.StoreAvailable);
        return ToLoginState(result);
    }

    public async Task<LoginAbuseState> EnsureLoginAllowedAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var state = await GetLoginStateAsync(username, cancellationToken);
        if (!state.Throttled)
            return state;

        _logger.LogWarning(
            ThrottledEvent,
            "Authentication abuse protection throttled flow {Flow} on {Dimension}.",
            "login",
            "target");
        SetRetryAfter(state.RetryAfter);
        throw new TooManyRequestException(
            "Too many requests. Please try again later."
        );
    }

    public async Task<LoginAbuseState> RecordLoginFailureAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var result = await _store.RecordFailureAsync(
            LoginFailureKey(username),
            _options.LoginAccountFailureLimit,
            _options.LoginAccountFailureWindow,
            cancellationToken);
        ObserveStore(result.StoreAvailable);
        return ToLoginState(result);
    }

    public Task DelayFailedLoginAsync(
        long failureCount,
        CancellationToken cancellationToken = default)
    {
        var delay = CalculateLoginDelay(failureCount);
        if (delay == TimeSpan.Zero)
            return Task.CompletedTask;
        return Task.Delay(delay, _timeProvider, cancellationToken);
    }

    internal TimeSpan CalculateLoginDelay(long failureCount)
    {
        if (failureCount <= 0)
            return TimeSpan.Zero;

        var exponent = Math.Min(failureCount - 1, 30);
        var multiplier = 1L << (int)exponent;
        var requestedTicks = _options.DelayBase.Ticks > long.MaxValue / multiplier
            ? long.MaxValue
            : _options.DelayBase.Ticks * multiplier;
        return TimeSpan.FromTicks(Math.Min(requestedTicks, _options.DelayMaximum.Ticks));
    }

    public async Task ResetLoginFailuresAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var available = await _store.DeleteAsync(LoginFailureKey(username), cancellationToken);
        ObserveStore(available);
        if (available)
        {
            _logger.LogInformation(
                LoginSuccessEvent,
                "Authentication succeeded and the account failure state was cleared."
            );
        }
    }

    internal string LoginFailureKey(string username) =>
        BuildKey(
            "login",
            "failures",
            AuthAbuseTargetKind.Username,
            UsernamePolicy.Normalize(username));

    internal static string HashIdentifier(AuthAbuseTargetKind kind, string normalizedValue)
    {
        var value = $"{kind.ToString().ToLowerInvariant()}:{normalizedValue}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private LoginAbuseState ToLoginState(AuthCounterResult result) => new(
        result.Count,
        result.Count >= _options.CaptchaFailureThreshold,
        !result.Allowed,
        result.StoreAvailable,
        result.RetryAfter);

    private void HandleDecision(
        AuthCounterResult result,
        AuthAbuseFlow flow,
        string dimension)
    {
        ObserveStore(result.StoreAvailable);
        if (!result.StoreAvailable || result.Allowed)
            return;

        _logger.LogWarning(
            ThrottledEvent,
            "Authentication abuse protection throttled flow {Flow} on {Dimension}.",
            FlowName(flow),
            dimension);
        SetRetryAfter(result.RetryAfter);
        throw new TooManyRequestException(
            "Too many requests. Please try again later."
        );
    }

    private void ObserveStore(bool available)
    {
        if (!available)
        {
            if (Interlocked.Exchange(ref _redisDegraded, 1) == 0)
            {
                _logger.LogCritical(
                    RedisUnavailableEvent,
                    "Redis authentication abuse protection is unavailable; local IP limiting remains active."
                );
            }
            return;
        }

        if (Interlocked.Exchange(ref _redisDegraded, 0) == 1)
        {
            _logger.LogInformation(
                RedisRecoveredEvent,
                "Redis authentication abuse protection recovered."
            );
        }
    }

    private void SetRetryAfter(TimeSpan? retryAfter)
    {
        if (retryAfter is null)
            return;

        var response = _httpContextAccessor.HttpContext?.Response;
        if (response is not null)
        {
            response.Headers.RetryAfter = Math.Max(
                1,
                (int)Math.Ceiling(retryAfter.Value.TotalSeconds)
            ).ToString();
        }
    }

    private (int Limit, TimeSpan Window) TargetBudget(AuthAbuseFlow flow) => flow switch
    {
        AuthAbuseFlow.Recovery => (
            _options.RecoveryTargetPermitLimit,
            _options.RecoveryTargetWindow),
        AuthAbuseFlow.Verification => (
            _options.VerificationTargetPermitLimit,
            _options.VerificationTargetWindow),
        AuthAbuseFlow.OAuthCompletion => (
            _options.OAuthCompletionTargetPermitLimit,
            _options.OAuthCompletionTargetWindow),
        AuthAbuseFlow.MfaDelivery => (
            _options.MfaDeliveryTargetPermitLimit,
            _options.MfaDeliveryTargetWindow),
        AuthAbuseFlow.EmailAvailability => (
            _options.EmailAvailabilityTargetPermitLimit,
            _options.EmailAvailabilityTargetWindow),
        _ => throw new InvalidOperationException($"Flow {flow} has no target budget."),
    };

    private string BuildKey(
        string flow,
        string dimension,
        AuthAbuseTargetKind kind,
        string normalizedValue) =>
        $"{_options.KeyPrefix}:{flow}:{dimension}:{HashIdentifier(kind, normalizedValue)}";

    private static string FlowName(AuthAbuseFlow flow) => flow switch
    {
        AuthAbuseFlow.Recovery => "recovery",
        AuthAbuseFlow.Verification => "verification",
        AuthAbuseFlow.OAuthCompletion => "oauth-completion",
        AuthAbuseFlow.MfaDelivery => "mfa-delivery",
        AuthAbuseFlow.EmailAvailability => "email-availability",
        _ => "general",
    };

    private static string NormalizeTarget(AuthAbuseTargetKind kind, string target) => kind switch
    {
        AuthAbuseTargetKind.Username => UsernamePolicy.Normalize(target),
        AuthAbuseTargetKind.Email => EmailPolicy.Normalize(target),
        _ => (target ?? string.Empty).Trim(),
    };

    private static string NormalizeIp(string sourceIp)
    {
        var value = (sourceIp ?? string.Empty).Trim();
        return IPAddress.TryParse(value, out var address)
            ? address.MapToIPv6().ToString()
            : "unknown";
    }
}
