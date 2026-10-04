using backend.main.shared.utilities.logger;

using Confluent.Kafka;

namespace backend.worker.media_worker;

public sealed class KafkaMediaWorker : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MediaWorkerOptions _options;
    private readonly Func<IConsumer<string, string>> _consumerFactory;

    public KafkaMediaWorker(
        IServiceScopeFactory scopeFactory,
        MediaWorkerOptions options)
        : this(scopeFactory, options, null)
    {
    }

    /// <summary>Lets tests drive the consume loop with a consumer of their own.</summary>
    internal KafkaMediaWorker(
        IServiceScopeFactory scopeFactory,
        MediaWorkerOptions options,
        Func<IConsumer<string, string>>? consumerFactory)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _consumerFactory = consumerFactory ?? BuildConsumer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured)
        {
            Logger.Warn(
                "Media worker is disabled because AZURE_STORAGE_CONNECTION_STRING, AZURE_STORAGE_CONTAINER_NAME, or AZURE_STORAGE_QUARANTINE_CONTAINER_NAME is not configured.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;

            try
            {
                using var consumer = _consumerFactory();
                consumer.Subscribe(_options.Topic);

                Logger.Info($"Kafka media worker subscribed to '{_options.Topic}'.");

                while (!stoppingToken.IsCancellationRequested)
                {
                    result = consumer.Consume(stoppingToken);
                    if (result?.Message == null)
                        continue;

                    var envelope = MediaWorkerEnvelope.FromConsumeResult(result);

                    using var scope = _scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<MediaWorkerMessageProcessor>();

                    await processor.ProcessAsync(envelope, stoppingToken);
                    consumer.Commit(result);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                Logger.Warn(ex, "Kafka media worker consumer error. Reconnecting soon...");
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Kafka media worker processing error. Retrying soon...");

                if (result != null)
                {
                    Logger.Info(
                        $"Media worker will retry topic {_options.Topic} at partition {result.Partition.Value}, offset {result.Offset.Value}.");
                }

                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
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
