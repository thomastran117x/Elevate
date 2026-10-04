namespace backend.main.shared.providers
{
    public interface IPublisher
    {
        Task PublishAsync<T>(string topic, T message);

        /// <summary>
        /// Publishes, giving up when <paramref name="cancellationToken"/> fires instead of waiting
        /// out the producer's own delivery timeout, which is minutes long. A publish abandoned this
        /// way may still be delivered.
        /// </summary>
        /// <remarks>
        /// Implementations that cannot cancel fall back to the uncancellable overload.
        /// </remarks>
        Task PublishAsync<T>(string topic, T message, CancellationToken cancellationToken) =>
            PublishAsync(topic, message);
    }
}
