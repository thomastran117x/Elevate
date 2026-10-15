using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;

namespace backend.main.features.auth.events;

public static class AuthSecurityEventEmitterExtensions
{
    /// <summary>Records a completed sign-in and passes the issued session through.</summary>
    public static UserToken SignedIn(
        this IAuthSecurityEventEmitter emitter,
        UserToken userToken,
        AuthSecurityMethod method,
        AuthSecurityFlow flow)
    {
        emitter.Emit(new AuthSecurityEvent
        {
            Definition = AuthSecurityEvents.SignInSucceeded,
            Outcome = AuthSecurityOutcome.Succeeded,
            Method = method,
            Flow = flow,
            UserId = userToken.user.Id,
            SessionId = userToken.token.SessionId,
        });

        return userToken;
    }

    /// <summary>
    /// Records a second-factor change that was refused. A refusal by an attempt limit is recorded
    /// as a throttle so throttling dashboards see every limit in one place.
    /// </summary>
    public static void FactorChangeFailed(
        this IAuthSecurityEventEmitter emitter,
        int userId,
        AuthSecurityFactor factor,
        AuthFactorChange change,
        Exception exception)
    {
        var (outcome, reason) = Classify(exception);
        var throttled = outcome == AuthSecurityOutcome.Throttled;
        emitter.Emit(new AuthSecurityEvent
        {
            Definition = throttled ? AuthSecurityEvents.Throttled : AuthSecurityEvents.FactorChangeFailed,
            Outcome = outcome,
            Reason = reason,
            Flow = AuthSecurityFlow.MfaEnrollment,
            Dimension = throttled ? AuthThrottleDimension.Attempts : null,
            Factor = factor,
            Change = change,
            UserId = userId,
        });
    }

    /// <summary>
    /// Classifies a rejected request from its exception type, never its message: messages can
    /// carry whatever the request supplied.
    /// </summary>
    public static (AuthSecurityOutcome Outcome, AuthSecurityReason Reason) Classify(Exception exception) =>
        exception switch
        {
            UnauthorizedException => (AuthSecurityOutcome.Denied, AuthSecurityReason.InvalidCode),
            TooManyRequestException => (AuthSecurityOutcome.Throttled, AuthSecurityReason.LimitExceeded),
            ConflictException => (AuthSecurityOutcome.Denied, AuthSecurityReason.InvalidState),
            BadRequestException => (AuthSecurityOutcome.Denied, AuthSecurityReason.InvalidRequest),
            AppException { StatusCode: < 500 } => (AuthSecurityOutcome.Denied, AuthSecurityReason.Rejected),
            _ => (AuthSecurityOutcome.Faulted, AuthSecurityReason.None),
        };
}
