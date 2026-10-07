using System.Reflection;

using backend.main.features.media;
using backend.main.shared.exceptions.http;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;

using Confluent.Kafka;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Media;

public class KafkaMediaValidationDispatcherTests
{
    private static readonly MediaAsset Asset = new()
    {
        Id = 7,
        PublicId = Guid.CreateVersion7(),
        QuarantineBlobPath = "events/upload.png",
        PublicUrl = "https://storage.test/public/events/upload.webp",
        DeclaredContentType = "image/png"
    };

    [Fact]
    public async Task DispatchAsync_ShouldPublishTheRequest_WithACancellableToken()
    {
        var publisher = new HangingPublisher { Hang = false };
        var dispatcher = Dispatcher(publisher);

        await dispatcher.DispatchAsync(Asset, 3, "Club images", CancellationToken.None);

        var (topic, message, token) = publisher.Calls.Should().ContainSingle().Subject;
        topic.Should().Be("media-validation");
        token.CanBeCanceled.Should().BeTrue("the publish is bounded, not left to the producer's five-minute timeout");
        var request = message.Should().BeOfType<MediaValidationRequestMessage>().Subject;
        request.Attempt.Should().Be(3);
        request.Subject.Should().Be("Club images");
    }

    [Fact]
    public async Task DispatchAsync_ShouldGiveUpQuickly_WhenTheBrokerDoesNotAnswer()
    {
        var dispatcher = Dispatcher(new HangingPublisher(), publishTimeout: TimeSpan.FromMilliseconds(50));

        var started = DateTime.UtcNow;

        var act = () => dispatcher.DispatchAsync(Asset, 1, "Event images", CancellationToken.None);

        await act.Should().ThrowAsync<NotAvailableException>()
            .WithMessage(KafkaMediaValidationDispatcher.UnavailableMessage);
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DispatchAsync_ShouldRethrowTheCallersCancellation_RatherThanBlameTheBroker()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var dispatcher = Dispatcher(new HangingPublisher(), publishTimeout: TimeSpan.FromMinutes(5));

        var act = () => dispatcher.DispatchAsync(Asset, 1, "Event images", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task IPublisher_ShouldFallBackToTheUncancellableOverload_ForImplementationsThatCannotCancel()
    {
        var publisher = new Mock<IPublisher> { CallBase = true };
        publisher.Setup(p => p.PublishAsync("topic", "message")).Returns(Task.CompletedTask);

        await publisher.Object.PublishAsync("topic", "message", CancellationToken.None);

        publisher.Verify(p => p.PublishAsync("topic", "message"), Times.Once);
    }

    [Fact]
    public async Task Publisher_ShouldHandTheTokenToTheProducer_WhenKafkaNativeLibraryIsAvailable()
    {
        try
        {
            await using var publisher = new Publisher();
            var producer = new Mock<IProducer<string, string>>();
            CancellationToken seen = default;
            producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Message<string, string>, CancellationToken>((_, _, token) => seen = token)
                .ReturnsAsync(new DeliveryResult<string, string>());
            typeof(Publisher).GetField("_producer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(publisher, producer.Object);
            using var cancellation = new CancellationTokenSource();

            await publisher.PublishAsync("topic", new { Value = 1 }, cancellation.Token);
            await publisher.PublishAsync("topic", new { Value = 2 });

            producer.Verify(
                p => p.ProduceAsync("topic", It.IsAny<Message<string, string>>(), cancellation.Token), Times.Once);
            seen.Should().Be(CancellationToken.None, "the uncancellable overload passes no token");
        }
        catch (DllNotFoundException)
        {
            return;
        }
    }

    private static KafkaMediaValidationDispatcher Dispatcher(IPublisher publisher, TimeSpan? publishTimeout = null) =>
        new(publisher, "media-validation", TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), publishTimeout);

    /// <summary>A broker that never acknowledges, unless told to, honouring cancellation like the producer does.</summary>
    private sealed class HangingPublisher : IPublisher
    {
        public bool Hang { get; init; } = true;

        public List<(string Topic, object? Message, CancellationToken Token)> Calls { get; } = [];

        public Task PublishAsync<T>(string topic, T message) =>
            PublishAsync(topic, message, CancellationToken.None);

        public async Task PublishAsync<T>(string topic, T message, CancellationToken cancellationToken)
        {
            Calls.Add((topic, message, cancellationToken));
            if (Hang)
                await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
