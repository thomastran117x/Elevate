using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Hosting;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

using worker.Config;
using worker.DTOs;
using worker.Interfaces;
using worker.Utilities;

namespace worker.Tasks;

public sealed class EmailWorker : BackgroundService, IAsyncDisposable
{
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly IEmailService _emailService;

    public EmailWorker(IEmailService emailService)
    {
        _emailService = emailService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConnectAsync(stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel!);

        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());

                var message = JsonSerializer.Deserialize<EmailMessage>(
                    json,
                    JsonOptions.Default
                );

                if (message is null)
                    throw new InvalidOperationException("Invalid email payload");

                await HandleEmailAsync(message);

                await _channel!.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[EmailWorker] Failed to process email message");
                await _channel!.BasicNackAsync(ea.DeliveryTag, false, true);
            }
        };

        await _channel!.BasicConsumeAsync(
            queue: "eventxperience-email",
            autoAck: false,
            consumer: consumer
        );

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private Task HandleEmailAsync(EmailMessage message)
    {
        return message.Type switch
        {
            EmailMessageType.VerifyEmail =>
                _emailService.SendVerificationEmailAsync(
                    message.Email,
                    message.Token,
                    message.Code
                ),

            EmailMessageType.ResetPassword =>
                _emailService.SendResetPasswordEmailAsync(
                    message.Email,
                    message.Token,
                    message.Code
                ),

            EmailMessageType.AccountConfirmation =>
                _emailService.SendConfirmationEmailAsync(
                    message.Email,
                    message.Token
                ),

            EmailMessageType.NewDevice =>
                _emailService.SendNewDeviceEmailAsync(
                    message.Email,
                    message.Token
                ),

            _ => throw new NotSupportedException(
                $"Unsupported email type: {message.Type}"
            )
        };
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(EnvManager.RabbitConnection),
            AutomaticRecoveryEnabled = true
        };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _connection = await factory.CreateConnectionAsync(ct);
                _channel = await _connection.CreateChannelAsync(null, ct);

                await _channel.QueueDeclareAsync(
                    queue: "eventxperience-email",
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: ct
                );

                await _channel.BasicQosAsync(0, 1, false, ct);

                Logger.Info("[EmailWorker] Connected to RabbitMQ");
                return;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "[EmailWorker] RabbitMQ unavailable, retrying...");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_channel != null)
                await _channel.CloseAsync();

            if (_connection != null)
                await _connection.CloseAsync();
        }
        catch { }
    }
}
