using backend.main.infrastructure.database.core;
using backend.main.shared.providers.messaging;
using backend.main.shared.utilities.logger;

using Confluent.Kafka;

using Microsoft.EntityFrameworkCore;

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
        await WaitForSchemaAsync(stoppingToken);

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

    /// <summary>
    /// Holds off consuming until the history table exists. The API applies migrations when it
    /// starts, and a worker that starts first would otherwise retry every message into the DLQ.
    /// </summary>
    private async Task WaitForSchemaAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();
                await db.AccountSecurityEvents.AsNoTracking().AnyAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Security history table is not ready ({ex.GetType().Name}). Waiting for the API to apply migrations...");
                try
                {
                    await Task.Delay(_retryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
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
