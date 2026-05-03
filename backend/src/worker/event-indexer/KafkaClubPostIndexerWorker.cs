using backend.main.utilities.implementation;

using Confluent.Kafka;

namespace backend.worker.event_indexer;

public sealed class KafkaClubPostIndexerWorker : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ClubPostIndexerOptions _options;

    public KafkaClubPostIndexerWorker(
        IServiceScopeFactory scopeFactory,
        ClubPostIndexerOptions options)
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

                Logger.Info($"Kafka club post indexer subscribed to '{_options.Topic}'.");

                while (!stoppingToken.IsCancellationRequested)
                {
                    result = consumer.Consume(stoppingToken);
                    if (result?.Message == null)
                        continue;

                    using var scope = _scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<ClubPostIndexerMessageProcessor>();

                    await processor.ProcessAsync(EventIndexerEnvelope.FromConsumeResult(result), stoppingToken);
                    consumer.Commit(result);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                Logger.Warn(ex, "Kafka club post indexer consumer error. Reconnecting soon...");
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Kafka club post indexer processing error. Retrying soon...");
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
