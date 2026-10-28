using backend.main.infrastructure.database.core;
using backend.main.shared.utilities.logger;

using Microsoft.EntityFrameworkCore;

namespace backend.worker.security_history_worker;

/// <summary>
/// Holds the worker's background services until the API has applied the migrations they need.
/// </summary>
/// <remarks>
/// The API migrates at startup and the worker never does, so either can come up first. Reading a
/// row of each table the worker uses checks every mapped column, not just that the table exists:
/// a worker started ahead of a migration that added columns would otherwise run against the old
/// schema and fail every query until the API caught up.
/// </remarks>
internal static class SecurityHistorySchema
{
    /// <returns>False when the host stopped before the schema was ready.</returns>
    public static async Task<bool> WaitUntilReadyAsync(
        IServiceScopeFactory scopeFactory,
        TimeSpan retryDelay,
        string waiter,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();
                await db.AccountSecurityEvents.AsNoTracking().Take(1).ToListAsync(stoppingToken);
                await db.SecurityHistoryOutbox.AsNoTracking().Take(1).ToListAsync(stoppingToken);
                return true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[{waiter}] Security history schema is not ready ({ex.GetType().Name}). Waiting for the API to apply migrations...");
                try
                {
                    await Task.Delay(retryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }

        return false;
    }
}
