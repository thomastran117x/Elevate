namespace backend.main.seeders
{
    public interface ISeeder
    {
        Task SeedAsync(CancellationToken cancellationToken = default);
    }
}
