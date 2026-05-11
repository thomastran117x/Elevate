
using backend.main.shared.utilities.logger;

using Confluent.Kafka;

namespace backend.worker.event_indexer;

public sealed class KafkaEventIndexerWorker : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EventIndexerOptions _options;

    public KafkaEventIndexerWorker(
        IServiceScopeFactory scopeFactory,
        EventIndexerOptions options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;

            try
            {
                using var consumer = BuildConsumer();
                consumer.Subscribe(_options.Topic);

                Logger.Info($"Kafka event indexer subscribed to '{_options.Topic}'.");

                while (!stoppingToken.IsCancellationRequested)
                {
                    result = consumer.Consume(stoppingToken);
                    if (result?.Message == null)
                        continue;

                    var envelope = EventIndexerEnvelope.FromConsumeResult(result);

                    using var scope = _scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<EventIndexerMessageProcessor>();

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
                Logger.Warn(ex, "Kafka event indexer consumer error. Reconnecting soon...");
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Kafka event indexer processing error. Retrying soon...");

                if (result != null)
                {
                    Logger.Info(
                        $"Event indexer will retry topic {_options.Topic} at partition {result.Partition.Value}, offset {result.Offset.Value}.");
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
