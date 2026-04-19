using System.Text.Json;

using backend.main.configurations.environment;
using backend.main.dtos.messages;
using backend.main.models.documents;
using backend.main.publishers.implementation;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Polly;
using Polly.Retry;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace backend.main.consumers
{
    public class EventIndexConsumer : BackgroundService
    {
        private const string MainQueue = "event-es-index";
        private const string DlqQueue = "event-es-index-dlq";

        private readonly IServiceScopeFactory _scopeFactory;

        private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(500),
                ShouldHandle = new PredicateBuilder().Handle<Exception>()
            })
            .Build();

        public EventIndexConsumer(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(EnvironmentSetting.RabbitConnection),
                AutomaticRecoveryEnabled = true
            };

            IConnection? connection = null;
            IChannel? channel = null;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    connection = await factory.CreateConnectionAsync(stoppingToken);
                    channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                    await channel.QueueDeclareAsync(
                        queue: DlqQueue,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        cancellationToken: stoppingToken
                    );

                    await channel.QueueDeclareAsync(
                        queue: MainQueue,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: new Dictionary<string, object?>
                        {
                            ["x-dead-letter-exchange"] = "",
                            ["x-dead-letter-routing-key"] = DlqQueue
                        },
                        cancellationToken: stoppingToken
                    );

                    await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += async (_, ea) => await HandleAsync(ea, channel);
                    await channel.BasicConsumeAsync(MainQueue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

                    Logger.Info("EventIndexConsumer started, listening on 'event-es-index'.");

                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "EventIndexConsumer lost RabbitMQ connection. Reconnecting in 5s...");
                    await Task.Delay(5000, stoppingToken);
                }
                finally
                {
                    if (channel != null) { try { await channel.CloseAsync(); } catch { } }
                    if (connection != null) { try { await connection.CloseAsync(); } catch { } }
                }
            }
        }

        private async Task HandleAsync(BasicDeliverEventArgs ea, IChannel channel)
        {
            EventIndexEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<EventIndexEvent>(ea.Body.Span, JsonOptions.Default);
                if (evt == null) throw new InvalidOperationException("Failed to deserialize EventIndexEvent.");

                await RetryPipeline.ExecuteAsync(async ct =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var searchService = scope.ServiceProvider.GetRequiredService<IEventSearchService>();

                    if (evt.Operation == "delete")
                    {
                        await searchService.DeleteAsync(evt.EventId);
                    }
                    else
                    {
                        Elastic.Clients.Elasticsearch.GeoLocation? geo = null;
                        if (evt.Latitude.HasValue && evt.Longitude.HasValue)
                        {
                            geo = Elastic.Clients.Elasticsearch.GeoLocation.LatitudeLongitude(
                                new Elastic.Clients.Elasticsearch.LatLonGeoLocation
                                {
                                    Lat = evt.Latitude.Value,
                                    Lon = evt.Longitude.Value
                                });
                        }

                        await searchService.IndexAsync(new EventDocument
                        {
                            Id = evt.EventId,
                            ClubId = evt.ClubId ?? 0,
                            Name = evt.Name ?? string.Empty,
                            Description = evt.Description ?? string.Empty,
                            Location = evt.Location ?? string.Empty,
                            IsPrivate = evt.IsPrivate ?? false,
                            StartTime = evt.StartTime ?? DateTime.UtcNow,
                            EndTime = evt.EndTime,
                            CreatedAt = evt.CreatedAt ?? DateTime.UtcNow,
                            UpdatedAt = evt.UpdatedAt ?? DateTime.UtcNow,
                            Category = (evt.Category ?? backend.main.models.enums.EventCategory.Other).ToString(),
                            VenueName = evt.VenueName,
                            City = evt.City,
                            Tags = evt.Tags ?? new List<string>(),
                            LocationGeo = geo,
                            RegistrationCount = evt.RegistrationCount ?? 0
                        });
                    }
                });

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                var eventId = evt?.EventId.ToString() ?? "unknown";
                Logger.Warn(ex, $"ES indexing failed after retries for event {eventId}. Sending to DLQ.");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        }
    }
}
