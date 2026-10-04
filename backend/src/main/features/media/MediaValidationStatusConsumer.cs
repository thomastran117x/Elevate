using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.utilities.logger;

using Confluent.Kafka;

namespace backend.main.features.media;

/// <summary>
/// Records what media-worker decided about each upload. The worker never touches the database;
/// this is where its verdict becomes a Ready or Rejected asset, through the same
/// <see cref="MediaValidationRecorder"/> the inline pipeline uses.
/// </summary>
/// <remarks>
/// Commits only after the outcome is recorded, so a database fault leaves the result to be read
/// again. A duplicate — Kafka redelivers — finds the asset already moved and changes nothing.
/// </remarks>
public sealed class MediaValidationStatusConsumer : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MediaValidationStatusConsumerOptions _options;
    private readonly Func<IConsumer<string, string>> _consumerFactory;

    public MediaValidationStatusConsumer(
        IServiceScopeFactory scopeFactory,
        MediaValidationStatusConsumerOptions options)
        : this(scopeFactory, options, null)
    {
    }

    /// <summary>Lets tests drive the consume loop with a consumer of their own.</summary>
    internal MediaValidationStatusConsumer(
        IServiceScopeFactory scopeFactory,
        MediaValidationStatusConsumerOptions options,
        Func<IConsumer<string, string>>? consumerFactory)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _consumerFactory = consumerFactory ?? BuildConsumer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;

            try
            {
                using var consumer = _consumerFactory();
                consumer.Subscribe(_options.Topic);

                while (!stoppingToken.IsCancellationRequested)
                {
                    result = consumer.Consume(stoppingToken);
                    if (result?.Message?.Value == null)
                        continue;

                    var message = Deserialize(result.Message.Value);
                    if (message == null)
                    {
                        consumer.Commit(result);
                        continue;
                    }

                    using var scope = _scopeFactory.CreateScope();
                    await RecordAsync(scope.ServiceProvider, message, stoppingToken);

                    consumer.Commit(result);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                Logger.Warn(ex, "Media validation status consumer error. Reconnecting soon...");
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Media validation status processing error. Reconnecting soon...");

                if (result != null)
                {
                    Logger.Info(
                        $"Media validation status consumer will retry topic {_options.Topic} at partition {result.Partition.Value}, offset {result.Offset.Value}.");
                }

                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    internal static async Task RecordAsync(
        IServiceProvider services,
        MediaValidationResultMessage message,
        CancellationToken cancellationToken)
    {
        var repository = services.GetRequiredService<IMediaAssetRepository>();
        var asset = await repository.GetByPublicIdAsync(message.MediaAssetId, cancellationToken);
        if (asset == null)
        {
            // Deleted with its uploader's account while the worker had it.
            Logger.Warn($"[MediaValidationStatusConsumer] Media asset {message.MediaAssetId} no longer exists; dropping its result.");
            return;
        }

        var recorder = services.GetRequiredService<MediaValidationRecorder>();
        await recorder.RecordAsync(asset, message.Attempt, message.ToOutcome());
    }

    private static MediaValidationResultMessage? Deserialize(string payload)
    {
        try
        {
            var message = JsonSerializer.Deserialize<MediaValidationResultMessage>(payload, JsonOptions.Default);
            if (message is { MediaAssetId: var id, Attempt: > 0 } && id != Guid.Empty)
                return message;
        }
        catch (JsonException)
        {
        }

        // Nothing to record and nothing a retry would change.
        Logger.Warn("[MediaValidationStatusConsumer] Skipping a media validation result that could not be read.");
        return null;
    }

    private IConsumer<string, string> BuildConsumer()
    {
        return new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            ClientId = _options.GroupId
        }).Build();
    }
}
