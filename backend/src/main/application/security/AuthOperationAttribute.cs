using backend.main.features.auth.events;

using Microsoft.AspNetCore.Mvc.Filters;

namespace backend.main.application.security
{
    /// <summary>
    /// Times an authentication endpoint into <c>auth.operation.duration</c>, tagged with the
    /// operation and whether it succeeded, was refused, was throttled, or faulted.
    /// </summary>
    /// <remarks>
    /// The outcome comes from the final status code, so an expected refusal (a wrong password, an
    /// expired code) is told apart from a service fault on a dashboard without the response telling
    /// the client anything new. A request the client abandoned is not recorded.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class AuthOperationAttribute : Attribute, IAsyncResourceFilter
    {
        public AuthOperationAttribute(AuthOperation operation)
        {
            Operation = operation;
        }

        public AuthOperation Operation
        {
            get;
        }

        public async Task OnResourceExecutionAsync(
            ResourceExecutingContext context,
            ResourceExecutionDelegate next)
        {
            var services = context.HttpContext.RequestServices;
            var metrics = services.GetService<AuthSecurityMetrics>();
            if (metrics is null)
            {
                await next();
                return;
            }

            var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
            var started = timeProvider.GetTimestamp();
            var faulted = false;
            try
            {
                var executed = await next();
                faulted = executed.Exception is not null && !executed.ExceptionHandled;
            }
            catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                faulted = true;
                throw;
            }
            finally
            {
                if (!context.HttpContext.RequestAborted.IsCancellationRequested)
                {
                    metrics.RecordOperation(
                        Operation,
                        Classify(context.HttpContext.Response.StatusCode, faulted),
                        timeProvider.GetElapsedTime(started));
                }
            }
        }

        internal static AuthSecurityOutcome Classify(int statusCode, bool faulted) => statusCode switch
        {
            _ when faulted || statusCode >= 500 => AuthSecurityOutcome.Faulted,
            StatusCodes.Status429TooManyRequests => AuthSecurityOutcome.Throttled,
            >= 400 => AuthSecurityOutcome.Denied,
            _ => AuthSecurityOutcome.Succeeded,
        };
    }
}
