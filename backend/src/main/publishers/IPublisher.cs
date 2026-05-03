namespace backend.main.publishers.interfaces
{
    public interface IPublisher
    {
        Task PublishAsync<T>(string topic, T message);
    }
}
