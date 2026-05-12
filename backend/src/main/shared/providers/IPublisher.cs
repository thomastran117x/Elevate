namespace backend.main.shared.providers
{
    public interface IPublisher
    {
        Task PublishAsync<T>(string topic, T message);
    }
}
