using System.Net;
using System.Text.Json;

using backend.main.features.auth.events;
using backend.main.features.auth.history;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.tests.Unit.Features.Auth.Events;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Features.Auth.History;

public class SecurityHistoryEventSinkTests
{
    private const string Topic = "security-history-test";
    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Write_ShouldPublishARecordedEvent_WithOnlyCoarseContext()
    {
        var publisher = new CapturingPublisher();
        var sink = CreateSink(publisher, Context("203.0.113.50", ChromeUserAgent));

        sink.Write(FactorChanged() with { ActorUserId = 99 });

        var (topic, message) = publisher.Published.Should().ContainSingle().Subject;
        topic.Should().Be(Topic);
        message.MessageId.Should().NotBeEmpty();
        message.UserId.Should().Be(7);
        message.Kind.Should().Be(AccountSecurityEventKind.MfaEnabled);
        message.EventName.Should().Be("auth.factor.changed");
        message.Method.Should().BeNull();
        message.Factor.Should().Be("totp");
        message.Reason.Should().BeNull();
        message.Client.Should().Be(SecurityHistoryClient.Chrome);
        message.DeviceClass.Should().Be(AuthDeviceClass.Desktop);
        message.Network.Should().Be("203.0.113.0/24");
        message.CorrelationId.Should().Be("req-123");
        message.InitiatedByAdmin.Should().BeTrue();
        message.OccurredAt.Should().Be(OccurredAt);
    }

    [Fact]
    public void Write_ShouldGiveEveryEventItsOwnMessageId()
    {
        var publisher = new CapturingPublisher();
        var sink = CreateSink(publisher, Context("203.0.113.50", ChromeUserAgent));

        sink.Write(FactorChanged());
        sink.Write(FactorChanged());

        publisher.Published.Select(p => p.Message.MessageId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Write_ShouldIgnoreEventsTheOwnerShouldNotSee()
    {
        var publisher = new CapturingPublisher();
        var sink = CreateSink(publisher, Context("203.0.113.50", ChromeUserAgent));

        sink.Write(FactorChanged() with { Definition = AuthSecurityEvents.SignInFailed });
        sink.Write(FactorChanged() with { UserId = null });

        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public void Write_ShouldPublishNothing_WhenPublishingIsOff()
    {
        var publisher = new CapturingPublisher();
        var sink = CreateSink(
            publisher,
            Context("203.0.113.50", ChromeUserAgent),
            new SecurityHistoryOptions { PublishEnabled = false });

        sink.Write(FactorChanged());

        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public void Write_ShouldLeaveTheNetworkOut_WhenMaskedNetworksAreOff()
    {
        var publisher = new CapturingPublisher();
        var sink = CreateSink(
            publisher,
            Context("203.0.113.50", ChromeUserAgent),
            new SecurityHistoryOptions { IncludeMaskedNetwork = false });

        sink.Write(FactorChanged());

        publisher.Published.Single().Message.Network.Should().BeNull();
        publisher.Published.Single().Message.Client.Should().Be(SecurityHistoryClient.Chrome);
    }

    [Fact]
    public void Write_ShouldStillRecord_WithoutARequest()
    {
        var publisher = new CapturingPublisher();
        var sink = CreateSink(publisher, context: null);

        sink.Write(FactorChanged());

        var message = publisher.Published.Single().Message;
        message.Client.Should().Be(SecurityHistoryClient.Unknown);
        message.Network.Should().BeNull();
    }

    [Fact]
    public void Write_ShouldStillRecord_WhenTheRequestHasEnded()
    {
        var context = new Mock<HttpContext>();
        context.SetupGet(c => c.Request).Throws(new ObjectDisposedException("HttpContext"));
        var publisher = new CapturingPublisher();
        var sink = CreateSink(publisher, context.Object);

        sink.Write(FactorChanged());

        publisher.Published.Single().Message.Client.Should().Be(SecurityHistoryClient.Unknown);
    }

    [Fact]
    public void Write_ShouldCountAndSwallowAFailedPublish_LoggingOnlyTheExceptionType()
    {
        var publisher = new Mock<IPublisher>();
        publisher
            .Setup(p => p.PublishAsync(Topic, It.IsAny<SecurityHistoryMessage>()))
            .ThrowsAsync(new InvalidOperationException("broker said: payload user 7"));
        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        using var recorder = new AuthMetricsRecorder(meterFactory);
        var logger = new ListLogger<SecurityHistoryEventSink>();
        var sink = CreateSink(
            publisher.Object,
            Context("203.0.113.50", ChromeUserAgent),
            metrics: new AuthSecurityMetrics(meterFactory),
            logger: logger);

        var write = () => sink.Write(FactorChanged());

        write.Should().NotThrow();
        recorder.Measurements.Should().ContainSingle(m => m.Instrument == "auth.security_history.publish_failures");
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(nameof(InvalidOperationException)).And.NotContain("broker said");
    }

    [Fact]
    public void Constructor_ShouldNotBuildThePublisher_AndAPublisherThatCannotLoadIsCounted()
    {
        var resolved = 0;
        var services = new ServiceCollection()
            .AddSingleton<IPublisher>(_ =>
            {
                resolved++;
                throw new DllNotFoundException("librdkafka");
            })
            .BuildServiceProvider();
        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        using var recorder = new AuthMetricsRecorder(meterFactory);

        // Hosts that build the emitter without ever recording (OpenAPI export) must not need Kafka.
        var sink = new SecurityHistoryEventSink(
            services,
            new HttpContextAccessor(),
            new StaticMonitor(new SecurityHistoryOptions()),
            new AuthSecurityMetrics(meterFactory),
            new ListLogger<SecurityHistoryEventSink>());
        resolved.Should().Be(0);

        var write = () => sink.Write(FactorChanged());

        write.Should().NotThrow();
        resolved.Should().Be(1);
        recorder.Measurements.Should().ContainSingle(m => m.Instrument == "auth.security_history.publish_failures");
    }

    [Fact]
    public void Message_ShouldCarryNoIdentifierBeyondTheAccountId()
    {
        // Adding a field here means deciding it is safe to keep for 90 days and show the owner.
        typeof(SecurityHistoryMessage).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            "MessageId",
            "UserId",
            "Kind",
            "EventName",
            "Method",
            "Factor",
            "Reason",
            "Client",
            "DeviceClass",
            "Network",
            "CorrelationId",
            "InitiatedByAdmin",
            "OccurredAt");
    }

    [Fact]
    public void Message_ShouldSerializeEnumsByName()
    {
        var publisher = new CapturingPublisher();
        CreateSink(publisher, Context("203.0.113.50", ChromeUserAgent)).Write(FactorChanged());

        var json = JsonSerializer.Serialize(publisher.Published.Single().Message, JsonOptions.Default);

        json.Should().Contain("\"kind\":\"MfaEnabled\"").And.Contain("\"client\":\"Chrome\"");
    }

    private static AuthSecurityEvent FactorChanged() => new()
    {
        Definition = AuthSecurityEvents.FactorChanged,
        Outcome = AuthSecurityOutcome.Succeeded,
        Factor = AuthSecurityFactor.Totp,
        Change = AuthFactorChange.Enabled,
        UserId = 7,
        SessionId = "0f8fad5bd9cb469fa16570867728950e",
        SourceIpHash = "hash-that-must-not-travel",
        ClientClass = AuthClientClass.Browser,
        DeviceClass = AuthDeviceClass.Desktop,
        CorrelationId = "req-123",
        OccurredAt = OccurredAt,
    };

    private static HttpContext Context(string remoteAddress, string userAgent)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        context.Request.Headers.UserAgent = userAgent;
        return context;
    }

    private static SecurityHistoryEventSink CreateSink(
        IPublisher publisher,
        HttpContext? context,
        SecurityHistoryOptions? options = null,
        AuthSecurityMetrics? metrics = null,
        ILogger<SecurityHistoryEventSink>? logger = null) =>
        new(
            publisher,
            new HttpContextAccessor { HttpContext = context },
            new StaticMonitor(options ?? new SecurityHistoryOptions()),
            metrics ?? new AuthSecurityMetrics(AuthSecurityEventTestSupport.CreateMeterFactory()),
            logger ?? new ListLogger<SecurityHistoryEventSink>(),
            Topic);

    private sealed class CapturingPublisher : IPublisher
    {
        public List<(string Topic, SecurityHistoryMessage Message)> Published { get; } = [];

        public Task PublishAsync<T>(string topic, T message)
        {
            Published.Add((topic, (SecurityHistoryMessage)(object)message!));
            return Task.CompletedTask;
        }
    }

    private sealed class StaticMonitor(SecurityHistoryOptions value) : IOptionsMonitor<SecurityHistoryOptions>
    {
        public SecurityHistoryOptions CurrentValue => value;

        public SecurityHistoryOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<SecurityHistoryOptions, string?> listener) => null;
    }
}
