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
            var consumer = _consumerFactory();
            try
            {
                consumer.Subscribe(_options.Topic);

                Logger.Info($"Kafka security history worker subscribed to '{_options.Topic}'.");

                while (!stoppingToken.IsCancellationRequested)
                {
                    var result = consumer.Consume(stoppingToken);
                    if (result?.Message == null)
                        continue;

                    if (!await TryProcessAsync(result, stoppingToken))
                    {
                        // Storing failed (the database is unavailable) or the DLQ publish failed.
                        // Nothing was committed, so rewind to this message and try it again on the
                        // same consumer: rebuilding it would leave and rejoin the group every time.
                        consumer.Seek(result.TopicPartitionOffset);
                        await Task.Delay(_retryDelay, stoppingToken);
                        continue;
                    }

                    consumer.Commit(result);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The consumer itself failed (a broker or group error); build a new one.
                Logger.Warn(ex, "Kafka security history worker consumer error. Reconnecting soon...");
                await DelayUnlessStoppingAsync(stoppingToken);
            }
            finally
            {
                CloseQuietly(consumer);
            }
        }
    }

    /// <returns>False when the message must be tried again; its offset is left uncommitted.</returns>
    private async Task<bool> TryProcessAsync(ConsumeResult<string, string> result, CancellationToken stoppingToken)
    {
        try
        {
            // One scope, and so one DbContext, per attempt.
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<SecurityHistoryMessageProcessor>();
            await processor.ProcessAsync(KafkaMessageEnvelope.FromConsumeResult(result), stoppingToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            Logger.Warn(ex, $"Kafka security history worker will retry partition {result.Partition.Value}, offset {result.Offset.Value}.");
            return false;
        }
    }

    private async Task DelayUnlessStoppingAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_retryDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Leaves the group cleanly so its partitions are reassigned at once, not after the session timeout.</summary>
    private static void CloseQuietly(IConsumer<string, string> consumer)
    {
        try
        {
            consumer.Close();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Kafka security history worker could not close its consumer cleanly ({ex.GetType().Name}).");
        }
        finally
        {
            consumer.Dispose();
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
