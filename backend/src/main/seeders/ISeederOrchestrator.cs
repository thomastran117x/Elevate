namespace backend.main.seeders
{
    public interface ISeederOrchestrator
    {
        Task RunAsync(CancellationToken cancellationToken = default);
    }
}
