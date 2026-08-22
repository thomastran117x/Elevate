using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

using Microsoft.AspNetCore.SignalR;

namespace backend.main.features.clubs.realtime;

/// <summary>
/// Translates domain exceptions thrown inside hub methods into <see cref="HubException"/>.
/// </summary>
/// <remarks>
/// The hub equivalent of <c>GlobalExceptionHandler</c>. SignalR only forwards the message of a
/// <see cref="HubException"/> to the client; anything else surfaces as an opaque
/// "An unexpected error occurred", which would hide "you must be a member of this club"
/// from a caller that needs to act on it. Unexpected exceptions are deliberately left
/// opaque and logged instead.
/// </remarks>
public sealed class ClubRealtimeExceptionFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (AppException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (Exception ex) when (ex is not HubException)
        {
            Logger.Error(
                ex,
                $"Unhandled exception in hub method '{invocationContext.HubMethodName}'.");
            throw;
        }
    }
}
