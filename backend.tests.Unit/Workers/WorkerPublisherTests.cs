using System.Reflection;
using System.Text.Json;

using backend.main.features.events.invitations;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.providers.messaging;
using backend.worker.email_worker;
using backend.worker.sms_worker;

using Confluent.Kafka;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Workers;

public class WorkerPublisherTests
{
    [Fact]
    public async Task EmailWorkerDlqPublisher_ShouldSerializeAndPublishPayload_WhenKafkaNativeLibraryIsAvailable()
    {
        try
        {
            var publisher = new KafkaEmailWorkerDlqPublisher(
                new EmailWorkerOptions("kafka", "email", "group", "email-dlq", "status", "smtp", 587, "user", "pass", "http://localhost")
            );
            var producer = new Mock<IProducer<string, string>>();
            Message<string, string>? publishedMessage = null;
            string? publishedTopic = null;
            producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Message<string, string>, CancellationToken>((topic, message, _) =>
                {
                    publishedTopic = topic;
                    publishedMessage = message;
                })
                .ReturnsAsync(new DeliveryResult<string, string>());
            SetProducerField(publisher, "_producer", producer.Object);

            await publisher.PublishAsync(CreateEnvelope("eventxperience-email"), "smtp down");
            await publisher.DisposeAsync();

            publishedTopic.Should().Be("email-dlq");
            publishedMessage.Should().NotBeNull();
            publishedMessage!.Key.Should().Be("1");
            JsonSerializer.Deserialize<KafkaWorkerDlqMessage>(publishedMessage.Value, JsonOptions.Default)!
                .Error.Should().Be("smtp down");
            producer.Verify(p => p.Dispose(), Times.Once);
        }
        catch (DllNotFoundException)
        {
            return;
        }
    }

    [Fact]
    public async Task SmsWorkerDlqPublisher_ShouldSerializeAndPublishPayload_WhenKafkaNativeLibraryIsAvailable()
    {
        try
        {
            var publisher = new KafkaSmsWorkerDlqPublisher(
                new SmsWorkerOptions("kafka", "sms", "group", "sms-dlq", "sid", "token", "mg-service", null)
            );
            var producer = new Mock<IProducer<string, string>>();
            Message<string, string>? publishedMessage = null;
            string? publishedTopic = null;
            producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Message<string, string>, CancellationToken>((topic, message, _) =>
                {
                    publishedTopic = topic;
                    publishedMessage = message;
                })
                .ReturnsAsync(new DeliveryResult<string, string>());
            SetProducerField(publisher, "_producer", producer.Object);

            await publisher.PublishAsync(CreateEnvelope("eventxperience-sms"), "twilio down");
            await publisher.DisposeAsync();

            publishedTopic.Should().Be("sms-dlq");
            publishedMessage.Should().NotBeNull();
            publishedMessage!.Key.Should().Be("1");
            JsonSerializer.Deserialize<KafkaWorkerDlqMessage>(publishedMessage.Value, JsonOptions.Default)!
                .Error.Should().Be("twilio down");
            producer.Verify(p => p.Dispose(), Times.Once);
        }
        catch (DllNotFoundException)
        {
            return;
        }
    }

    [Fact]
    public async Task EmailDeliveryStatusPublisher_ShouldSerializeAndPublishStatusMessage_WhenKafkaNativeLibraryIsAvailable()
    {
        try
        {
            var publisher = new KafkaEmailDeliveryStatusPublisher(
                new EmailWorkerOptions("kafka", "email", "group", "email-dlq", "status-topic", "smtp", 587, "user", "pass", "http://localhost")
            );
            var producer = new Mock<IProducer<string, string>>();
            Message<string, string>? publishedMessage = null;
            string? publishedTopic = null;
            producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Message<string, string>, CancellationToken>((topic, message, _) =>
                {
                    publishedTopic = topic;
                    publishedMessage = message;
                })
                .ReturnsAsync(new DeliveryResult<string, string>());
            SetProducerField(publisher, "_producer", producer.Object);

            await publisher.PublishAsync(new EmailDeliveryStatusMessage
            {
                Type = EmailMessageType.NewDevice,
                EventInvitationId = 99,
                DeliveryStatus = EventInvitationDeliveryStatus.Sent
            });
            await publisher.DisposeAsync();

            publishedTopic.Should().Be("status-topic");
            publishedMessage.Should().NotBeNull();
            JsonSerializer.Deserialize<EmailDeliveryStatusMessage>(publishedMessage!.Value, JsonOptions.Default)!
                .EventInvitationId.Should().Be(99);
            producer.Verify(p => p.Dispose(), Times.Once);
        }
        catch (DllNotFoundException)
        {
            return;
        }
    }

    private static KafkaMessageEnvelope CreateEnvelope(string topic)
    {
        return new KafkaMessageEnvelope(
            topic,
            0,
            1,
            null,
            "{}",
            new Dictionary<string, string?>());
    }

    private static void SetProducerField(object instance, string fieldName, IProducer<string, string> producer)
    {
        instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, producer);
    }
}
