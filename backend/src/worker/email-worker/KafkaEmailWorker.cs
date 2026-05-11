using backend.main.shared.utilities.logger;

using Confluent.Kafka;

namespace backend.worker.email_worker;

public sealed class KafkaEmailWorker : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailWorkerOptions _options;

    public KafkaEmailWorker(
        IServiceScopeFactory scopeFactory,
        EmailWorkerOptions options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured)
        {
            Logger.Warn("Email worker is disabled because SMTP_SERVER, EMAIL_USER, or EMAIL_PASSWORD is not configured.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var consumer = BuildConsumer();
                consumer.Subscribe(_options.Topic);

                Logger.Info($"Kafka email worker subscribed to '{_options.Topic}'.");

                while (!stoppingToken.IsCancellationRequested)
                {
                    var result = consumer.Consume(stoppingToken);
                    if (result?.Message == null)
                        continue;

                    using var scope = _scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<EmailMessageProcessor>();

                    await processor.ProcessAsync(EmailWorkerEnvelope.FromConsumeResult(result), stoppingToken);
                    consumer.Commit(result);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                Logger.Warn(ex, "Kafka email worker consumer error. Reconnecting soon...");
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Kafka email worker processing error. Retrying soon...");
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
