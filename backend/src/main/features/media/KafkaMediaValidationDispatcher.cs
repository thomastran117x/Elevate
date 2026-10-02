using backend.main.application.environment;
using backend.main.shared.exceptions.http;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.utilities.logger;

namespace backend.main.features.media;

/// <summary>
/// Hands a claimed asset to media-worker, which validates it off the request path and reports
/// back through <see cref="MediaValidationStatusConsumer"/>.
/// </summary>
/// <remarks>
/// Publishes directly rather than through an outbox. An outbox makes a publish atomic with a
/// database write because nothing else would record that the work is owed; here the asset row
/// already does. A request lost between the claim and the broker leaves the asset Processing,
/// and <see cref="MediaValidationReconciler"/> re-drives it.
/// </remarks>
public sealed class KafkaMediaValidationDispatcher : IMediaValidationDispatcher
{
    /// <summary>
    /// How long an attach waits for the worker's verdict. The pipeline takes well under a second
    /// and the round trip through Kafka a little more; a default request timeout of 30 s leaves
    /// room for a save that attaches a few images in turn.
    /// </summary>
    internal static readonly TimeSpan DefaultSettleWait = TimeSpan.FromSeconds(10);

    internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    internal const string UnavailableMessage = "Images can't be checked right now. Try again in a moment.";

    private readonly IPublisher _publisher;
    private readonly string _topic;

    public KafkaMediaValidationDispatcher(IPublisher publisher)
        : this(publisher, EnvironmentSetting.MediaValidationTopic, DefaultSettleWait, DefaultPollInterval)
    {
    }

    internal KafkaMediaValidationDispatcher(
        IPublisher publisher,
        string topic,
        TimeSpan settleWait,
        TimeSpan pollInterval)
    {
        _publisher = publisher;
        _topic = topic;
        SettleWait = settleWait;
        PollInterval = pollInterval;
    }

    public TimeSpan SettleWait
    {
        get;
    }

    public TimeSpan PollInterval
    {
        get;
    }

    public async Task DispatchAsync(MediaAsset asset, int attempt, string subject, CancellationToken cancellationToken)
    {
        var request = new MediaValidationRequestMessage
        {
            MediaAssetId = asset.PublicId,
            Attempt = attempt,
            QuarantineBlobPath = asset.QuarantineBlobPath ?? string.Empty,
            PublicUrl = asset.PublicUrl ?? string.Empty,
            DeclaredContentType = asset.DeclaredContentType,
            Subject = subject
        };

        try
        {
            await _publisher.PublishAsync(_topic, request);
        }
        catch (Exception ex)
        {
            // The caller releases the claim, so the user's retry can try again straight away.
            Logger.Warn(ex, $"[KafkaMediaValidationDispatcher] Could not request validation of media asset {asset.PublicId}.");
            throw new NotAvailableException(UnavailableMessage);
        }
    }
}
