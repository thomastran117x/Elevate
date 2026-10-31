using System.Net;
using System.Text.Json;

using backend.main.features.auth.events;
using backend.main.features.auth.history;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.tests.Unit.Features.Auth.Events;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Features.Auth.History;

public class SecurityHistoryEventSinkTests
{
    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Write_ShouldQueueARecordedEvent_WithOnlyCoarseContext()
    {
        var queue = new SecurityHistoryQueue();
        var sink = CreateSink(queue, Context("203.0.113.50", ChromeUserAgent));

        sink.Write(FactorChanged());

        var message = Queued(queue).Should().ContainSingle().Subject;
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
        message.InitiatedByAdmin.Should().BeFalse();
        message.OccurredAt.Should().Be(OccurredAt);
    }

    [Fact]
    public void Write_ShouldKeepTheAdministratorsRequestContextOutOfTheOwnersHistory()
    {
        var queue = new SecurityHistoryQueue();
        var sink = CreateSink(queue, Context("198.51.100.7", ChromeUserAgent));

        sink.Write(new AuthSecurityEvent
        {
            Definition = AuthSecurityEvents.AccountStatusChanged,
            Outcome = AuthSecurityOutcome.Succeeded,
            Reason = AuthSecurityReason.AccountDisabled,
            UserId = 7,
            ActorUserId = 99,
            DeviceClass = AuthDeviceClass.Desktop,
            CorrelationId = "req-admin",
            OccurredAt = OccurredAt,
        });

        // The request is the administrator's; the owner learns only that an administrator acted.
        var message = Queued(queue).Single();
        message.Kind.Should().Be(AccountSecurityEventKind.AccountDisabled);
        message.InitiatedByAdmin.Should().BeTrue();
        message.Client.Should().Be(SecurityHistoryClient.Unknown);
        message.DeviceClass.Should().Be(AuthDeviceClass.Unknown);
        message.Network.Should().BeNull();
        message.CorrelationId.Should().Be("req-admin");
    }

    [Fact]
    public void Write_ShouldGiveEveryEventItsOwnMessageId()
    {
        var queue = new SecurityHistoryQueue();
        var sink = CreateSink(queue, Context("203.0.113.50", ChromeUserAgent));

        sink.Write(FactorChanged());
        sink.Write(FactorChanged());

        Queued(queue).Select(m => m.MessageId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Write_ShouldIgnoreEventsTheOwnerShouldNotSee()
    {
        var queue = new SecurityHistoryQueue();
        var sink = CreateSink(queue, Context("203.0.113.50", ChromeUserAgent));

        sink.Write(FactorChanged() with { Definition = AuthSecurityEvents.SignInFailed });
        sink.Write(FactorChanged() with { UserId = null });

        Queued(queue).Should().BeEmpty();
    }

    [Fact]
    public void Write_ShouldQueueNothing_WhenPublishingIsOff()
    {
        var queue = new SecurityHistoryQueue();
        var sink = CreateSink(
            queue,
            Context("203.0.113.50", ChromeUserAgent),
            new SecurityHistoryOptions { PublishEnabled = false });

        sink.Write(FactorChanged());

        Queued(queue).Should().BeEmpty();
    }

    [Fact]
    public void Write_ShouldLeaveTheNetworkOut_WhenMaskedNetworksAreOff()
    {
        var queue = new SecurityHistoryQueue();
        var sink = CreateSink(
            queue,
            Context("203.0.113.50", ChromeUserAgent),
            new SecurityHistoryOptions { IncludeMaskedNetwork = false });

        sink.Write(FactorChanged());

        var message = Queued(queue).Single();
        message.Network.Should().BeNull();
        message.Client.Should().Be(SecurityHistoryClient.Chrome);
    }

    [Fact]
    public void Write_ShouldStillRecord_WithoutARequest()
    {
        var queue = new SecurityHistoryQueue();
        var sink = CreateSink(queue, context: null);

        sink.Write(FactorChanged());

        var message = Queued(queue).Single();
        message.Client.Should().Be(SecurityHistoryClient.Unknown);
        message.Network.Should().BeNull();
    }

    [Fact]
    public void Write_ShouldStillRecord_WhenTheRequestHasEnded()
    {
        var context = new Mock<HttpContext>();
        context.SetupGet(c => c.Request).Throws(new ObjectDisposedException("HttpContext"));
        var queue = new SecurityHistoryQueue();
        var sink = CreateSink(queue, context.Object);

        sink.Write(FactorChanged());

        Queued(queue).Single().Client.Should().Be(SecurityHistoryClient.Unknown);
    }

    [Fact]
    public void Write_ShouldCountEveryRefusedEntry_ButLogOnlyWhenTheQueueFillsAndDrains()
    {
        var queue = new SecurityHistoryQueue();
        while (queue.TryEnqueue(new SecurityHistoryMessage
        {
            MessageId = Guid.NewGuid(),
            UserId = 1,
            Kind = AccountSecurityEventKind.SignedIn,
            EventName = "auth.sign_in.succeeded",
            OccurredAt = OccurredAt,
        }))
        {
        }

        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        using var recorder = new AuthMetricsRecorder(meterFactory);
        var logger = new ListLogger<SecurityHistoryEventSink>();
        var sink = CreateSink(
            queue,
            Context("203.0.113.50", ChromeUserAgent),
            metrics: new AuthSecurityMetrics(meterFactory),
            logger: logger);

        var write = () =>
        {
            for (var i = 0; i < 50; i++)
                sink.Write(FactorChanged());
        };

        // The request carries on: a sink must never fail the action that produced the event.
        write.Should().NotThrow();
        recorder.Measurements.Count(m => m.Instrument == "auth.security_history.dropped").Should().Be(50);
        // One warning for the outage, not one per request.
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);

        queue.TakeBatch(5);
        sink.Write(FactorChanged());
        sink.Write(FactorChanged());

        logger.Entries.Should().HaveCount(2);
        logger.Entries[1].Level.Should().Be(LogLevel.Information);
        logger.Entries[1].Message.Should().Contain("accepting entries again");
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
        var queue = new SecurityHistoryQueue();
        CreateSink(queue, Context("203.0.113.50", ChromeUserAgent)).Write(FactorChanged());

        var json = JsonSerializer.Serialize(Queued(queue).Single(), JsonOptions.Default);

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
        SecurityHistoryQueue queue,
        HttpContext? context,
        SecurityHistoryOptions? options = null,
        AuthSecurityMetrics? metrics = null,
        ILogger<SecurityHistoryEventSink>? logger = null) =>
        new(
            queue,
            new HttpContextAccessor { HttpContext = context },
            new StaticMonitor(options ?? new SecurityHistoryOptions()),
            metrics ?? new AuthSecurityMetrics(AuthSecurityEventTestSupport.CreateMeterFactory()),
            logger ?? new ListLogger<SecurityHistoryEventSink>());

    /// <summary>Everything the sink queued, in order.</summary>
    private static IReadOnlyList<SecurityHistoryMessage> Queued(SecurityHistoryQueue queue) =>
        queue.TakeBatch(int.MaxValue);

    private sealed class StaticMonitor(SecurityHistoryOptions value) : IOptionsMonitor<SecurityHistoryOptions>
    {
        public SecurityHistoryOptions CurrentValue => value;

        public SecurityHistoryOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<SecurityHistoryOptions, string?> listener) => null;
    }
}
