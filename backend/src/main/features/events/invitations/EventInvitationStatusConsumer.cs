using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.utilities.logger;

using Confluent.Kafka;

namespace backend.main.features.events.invitations;

public sealed class EventInvitationStatusConsumer : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EventInvitationStatusConsumerOptions _options;

    public EventInvitationStatusConsumer(
        IServiceScopeFactory scopeFactory,
        EventInvitationStatusConsumerOptions options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;

            try
            {
                using var consumer = BuildConsumer();
                consumer.Subscribe(_options.Topic);

                while (!stoppingToken.IsCancellationRequested)
                {
                    result = consumer.Consume(stoppingToken);
                    if (result?.Message?.Value == null)
                        continue;

                    var message = JsonSerializer.Deserialize<EmailDeliveryStatusMessage>(
                        result.Message.Value,
                        JsonOptions.Default);

                    if (message?.EventInvitationId == null)
                    {
                        consumer.Commit(result);
                        continue;
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IEventInvitationService>();
                    await service.MarkInvitationDeliveryStatusAsync(
                        message.EventInvitationId.Value,
                        message.DeliveryStatus,
                        message.ErrorMessage);

                    consumer.Commit(result);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                Logger.Warn(ex, "Invitation delivery status consumer error. Reconnecting soon...");
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Invitation delivery status processing error. Reconnecting soon...");

                if (result != null)
                {
                    Logger.Info(
                        $"Invitation delivery status consumer will retry topic {_options.Topic} at partition {result.Partition.Value}, offset {result.Offset.Value}.");
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
