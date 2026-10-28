using backend.main.shared.providers.messaging;
using backend.main.shared.utilities.logger;

using Confluent.Kafka;

namespace backend.worker.security_history_worker;

public sealed class KafkaSecurityHistoryWorker : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SecurityHistoryWorkerOptions _options;
    private readonly Func<IConsumer<string, string>> _consumerFactory;
    private readonly TimeSpan _retryDelay;

    public KafkaSecurityHistoryWorker(
        IServiceScopeFactory scopeFactory,
        SecurityHistoryWorkerOptions options)
        : this(scopeFactory, options, null, ReconnectDelay)
    {
    }

    /// <summary>Lets tests drive the consume loop with a consumer of their own.</summary>
    internal KafkaSecurityHistoryWorker(
        IServiceScopeFactory scopeFactory,
        SecurityHistoryWorkerOptions options,
        Func<IConsumer<string, string>>? consumerFactory,
        TimeSpan retryDelay)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _consumerFactory = consumerFactory ?? BuildConsumer;
        _retryDelay = retryDelay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Consuming before the API's migration would retry every message against the old schema.
        if (!await SecurityHistorySchema.WaitUntilReadyAsync(_scopeFactory, _retryDelay, nameof(KafkaSecurityHistoryWorker), stoppingToken))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var consumer = _consumerFactory();
                consumer.Subscribe(_options.Topic);

                Logger.Info($"Kafka security history worker subscribed to '{_options.Topic}'.");

                while (!stoppingToken.IsCancellationRequested)
                {
                    var result = consumer.Consume(stoppingToken);
                    if (result?.Message == null)
                        continue;

                    // One scope, and so one DbContext, per message.
                    using var scope = _scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<SecurityHistoryMessageProcessor>();

                    await processor.ProcessAsync(KafkaMessageEnvelope.FromConsumeResult(result), stoppingToken);
                    consumer.Commit(result);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                Logger.Warn(ex, "Kafka security history worker consumer error. Reconnecting soon...");
                await Task.Delay(_retryDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                // Only a DLQ publish failure reaches here; the offset stays uncommitted and the
                // message is processed again after reconnecting.
                Logger.Warn(ex, "Kafka security history worker processing error. Retrying soon...");
                await Task.Delay(_retryDelay, stoppingToken);
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
