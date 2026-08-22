using backend.main.shared.utilities.logger;

using Microsoft.AspNetCore.SignalR;

namespace backend.main.features.clubs.realtime;

/// <summary>
/// Clears typing indicators whose TTL lapsed and tells the affected threads.
/// </summary>
/// <remarks>
/// Covers the case a client cannot: a tab closed mid-keystroke, or a network drop, so the
/// explicit "stopped typing" call never arrives. Without this the indicator would stick
/// until the connection itself timed out.
/// </remarks>
public sealed class TypingExpirySweeper : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(2);

    private readonly IClubPresenceStore _presence;
    private readonly IHubContext<ClubRealtimeHub> _hub;

    public TypingExpirySweeper(IClubPresenceStore presence, IHubContext<ClubRealtimeHub> hub)
    {
        _presence = presence;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                foreach (var threadKey in _presence.ExpireTyping(DateTimeOffset.UtcNow))
                {
                    await _hub.Clients
                        .Group(threadKey)
                        .SendAsync(
                            ClubRealtimeEvents.TypingChanged,
                            _presence.Typing(threadKey),
                            stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failed sweep must not kill the loop; the next tick retries.
                Logger.Error(ex, "Typing expiry sweep failed.");
            }
        }
    }
}
