using backend.main.application.features;
using backend.main.shared.utilities.logger;

namespace backend.main.shared.storage;

/// <summary>
/// Checks once, at startup, that the blob containers this deployment needs exist, and says how
/// to create them when they do not.
/// </summary>
/// <remarks>
/// The API no longer creates containers on the request path: that cost a round trip per upload
/// and needed a credential allowed to create containers. The price is that an environment nobody
/// provisioned fails its first upload. This makes that missed step loud in the startup log
/// instead of a confusing error in front of a user.
/// <para>
/// Reports rather than fails. An API that cannot store images can still serve everything else,
/// and refusing to start over it would turn a missing container into a full outage.
/// </para>
/// </remarks>
public sealed class BlobStorageStartupCheck : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IFeatureFlagEvaluator _featureFlags;

    public BlobStorageStartupCheck(IServiceProvider services, IFeatureFlagEvaluator featureFlags)
    {
        _services = services;
        _featureFlags = featureFlags;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _services.CreateScope();
            var blobService = scope.ServiceProvider.GetRequiredService<IAzureBlobService>();
            var missing = await blobService.FindMissingContainersAsync(
                _featureFlags.IsEnabled(FeatureFlagKeys.StorageQuarantine), stoppingToken);

            if (missing.Count == 0)
                return;

            Logger.Error(
                $"[BlobStorageStartupCheck] Missing blob container(s): {string.Join(", ", missing)}. " +
                "Image uploads will fail until they exist. Create them with " +
                "'dotnet run --project tools/Event.DevTasks -- storage-provision' " +
                "or see docs/DEPLOYMENT.md#blob-storage.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "[BlobStorageStartupCheck] Could not check the blob containers.");
        }
    }
}
