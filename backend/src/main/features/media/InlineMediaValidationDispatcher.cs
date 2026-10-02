using backend.main.shared.storage;

namespace backend.main.features.media;

/// <summary>
/// Runs <see cref="MediaValidationPipeline"/> inside the attach request and records the result
/// before returning, so the asset has settled by the time the attach reads it again.
/// </summary>
public sealed class InlineMediaValidationDispatcher : IMediaValidationDispatcher
{
    private readonly MediaValidationPipeline _pipeline;
    private readonly MediaValidationRecorder _recorder;

    public InlineMediaValidationDispatcher(MediaValidationPipeline pipeline, MediaValidationRecorder recorder)
    {
        _pipeline = pipeline;
        _recorder = recorder;
    }

    public TimeSpan SettleWait => TimeSpan.Zero;

    public TimeSpan PollInterval => TimeSpan.Zero;

    public async Task DispatchAsync(MediaAsset asset, int attempt, string subject, CancellationToken cancellationToken)
    {
        var outcome = await _pipeline.RunAsync(
            asset.QuarantineBlobPath ?? string.Empty,
            asset.PublicUrl!,
            asset.DeclaredContentType,
            subject,
            cancellationToken);

        await _recorder.RecordAsync(asset, attempt, outcome);
    }
}
