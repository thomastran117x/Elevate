using System.Diagnostics.Metrics;
using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.providers.messaging;
using backend.worker.email_worker;
using backend.worker.sms_worker;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace backend.tests.Unit.Workers;

/// <summary>
/// The delivery counters behind the recovery and MFA delivery dashboard: closed tag sets only,
/// recorded once per message by the email and SMS workers.
/// </summary>
public sealed class NotificationDeliveryMetricsTests : IDisposable
{
    private readonly ServiceProvider _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly NotificationDeliveryMetrics _metrics;
    private readonly MeterListener _listener = new();
    private readonly List<Measurement> _measurements = [];

    public NotificationDeliveryMetricsTests()
    {
        var factory = _provider.GetRequiredService<IMeterFactory>();
        _metrics = new NotificationDeliveryMetrics(factory);
        var meter = factory.Create(NotificationDeliveryMetrics.MeterName);
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var captured = new Dictionary<string, string?>();
            foreach (var tag in tags)
                captured[tag.Key] = tag.Value as string;

            lock (_measurements)
                _measurements.Add(new Measurement(instrument.Name, value, captured));
        });
        _listener.Start();
    }

    [Fact]
    public void EmailMessageTypes_ShouldBeReportedInSnakeCase()
    {
        NotificationDeliveryMetrics.WireName(EmailMessageType.ResetPassword).Should().Be("reset_password");
        NotificationDeliveryMetrics.WireName(EmailMessageType.MfaCode).Should().Be("mfa_code");
        NotificationDeliveryMetrics.WireName(EmailMessageType.SecurityAlert).Should().Be("security_alert");
        Enum.GetValues<EmailMessageType>().Select(NotificationDeliveryMetrics.WireName)
            .Should().OnlyHaveUniqueItems()
            .And.OnlyContain(name => name.All(character => char.IsLower(character) || character == '_'));
    }

    [Fact]
    public async Task EmailWorker_ShouldRecordASentDelivery_WithItsMessageType()
    {
        var processor = new EmailMessageProcessor(
            Mock.Of<IEmailSender>(), Mock.Of<IEmailWorkerDlqPublisher>(), Mock.Of<IEmailDeliveryStatusPublisher>(), _metrics);

        await processor.ProcessAsync(Envelope(new EmailMessage
        {
            Email = "delivery-canary@example.com",
            Type = EmailMessageType.MfaCode,
            Token = "424242",
        }));

        _measurements.Should().ContainSingle().Which.Should().BeEquivalentTo(new Measurement(
            "notifications.email.deliveries",
            1,
            new Dictionary<string, string?> { ["message_type"] = "mfa_code", ["outcome"] = "sent" }));
    }

    [Fact]
    public async Task EmailWorker_ShouldRecordAFailedDelivery_WithoutTheRecipientOrError()
    {
        var sender = new Mock<IEmailSender>();
        sender.Setup(service => service.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp refused delivery-canary@example.com"));
        var processor = new EmailMessageProcessor(
            sender.Object, Mock.Of<IEmailWorkerDlqPublisher>(), Mock.Of<IEmailDeliveryStatusPublisher>(), _metrics);

        await processor.ProcessAsync(Envelope(new EmailMessage
        {
            Email = "delivery-canary@example.com",
            Type = EmailMessageType.ResetPassword,
            Token = "reset-token-canary",
        }));

        var measurement = _measurements.Should().ContainSingle().Subject;
        measurement.Tags.Should().Equal(new Dictionary<string, string?>
        {
            ["message_type"] = "reset_password",
            ["outcome"] = "failed",
        });
        measurement.Tags.Values.Should().NotContain(value => value!.Contains("canary"));
    }

    [Fact]
    public async Task EmailWorker_ShouldRecordAMalformedPayload_AsAnUnknownType()
    {
        var processor = new EmailMessageProcessor(
            Mock.Of<IEmailSender>(), Mock.Of<IEmailWorkerDlqPublisher>(), Mock.Of<IEmailDeliveryStatusPublisher>(), _metrics);

        await processor.ProcessAsync(new KafkaMessageEnvelope("eventxperience-email", 0, 1, null, "{not-json}", new Dictionary<string, string?>()));

        _measurements.Should().ContainSingle().Which.Tags.Should().Equal(new Dictionary<string, string?>
        {
            ["message_type"] = "unknown",
            ["outcome"] = "malformed",
        });
    }

    [Theory]
    [InlineData(false, "sent")]
    [InlineData(true, "failed")]
    public async Task SmsWorker_ShouldRecordEachDeliveryOutcome(bool fails, string outcome)
    {
        var sender = new Mock<ISmsSender>();
        if (fails)
        {
            sender.Setup(service => service.SendAsync(It.IsAny<SmsMfaMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("rejected +14165550123"));
        }

        var processor = new SmsMfaMessageProcessor(sender.Object, Mock.Of<ISmsWorkerDlqPublisher>(), _metrics);

        await processor.ProcessAsync(new KafkaMessageEnvelope(
            "eventxperience-sms",
            0,
            1,
            null,
            JsonSerializer.Serialize(new SmsMfaMessage
            {
                PhoneNumber = "+14165550123",
                Code = "123456",
                Challenge = "challenge",
                Purpose = "mfa",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
            }, JsonOptions.Default),
            new Dictionary<string, string?>()));

        _measurements.Should().ContainSingle().Which.Should().BeEquivalentTo(new Measurement(
            "notifications.sms.deliveries",
            1,
            new Dictionary<string, string?> { ["outcome"] = outcome }));
    }

    [Fact]
    public async Task SmsWorker_ShouldRecordAMalformedPayload()
    {
        var processor = new SmsMfaMessageProcessor(Mock.Of<ISmsSender>(), Mock.Of<ISmsWorkerDlqPublisher>(), _metrics);

        await processor.ProcessAsync(new KafkaMessageEnvelope("eventxperience-sms", 0, 1, null, "{not-json}", new Dictionary<string, string?>()));

        _measurements.Should().ContainSingle().Which.Tags["outcome"].Should().Be("malformed");
    }

    public void Dispose()
    {
        _listener.Dispose();
        _provider.Dispose();
    }

    private static KafkaMessageEnvelope Envelope(EmailMessage message) =>
        new("eventxperience-email", 0, 1, null, JsonSerializer.Serialize(message, JsonOptions.Default), new Dictionary<string, string?>());

    private sealed record Measurement(string Instrument, long Value, Dictionary<string, string?> Tags);
}
