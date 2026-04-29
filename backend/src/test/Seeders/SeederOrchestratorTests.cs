using backend.main.seeders;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace backend.test;

public class SeederOrchestratorTests
{
    [Fact]
    public async Task RunAsync_RunsRegisteredSeeders_InDevelopment()
    {
        var seeder = new Mock<ISeeder>();
        var orchestrator = CreateOrchestrator(
            [seeder.Object],
            environmentName: Environments.Development
        );

        await orchestrator.RunAsync();

        seeder.Verify(seed => seed.SeedAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_SkipsSeeders_InNonDevelopment_WhenNoExplicitFlagIsSet()
    {
        var seeder = new Mock<ISeeder>();
        var orchestrator = CreateOrchestrator(
            [seeder.Object],
            environmentName: Environments.Production
        );

        await orchestrator.RunAsync();

        seeder.Verify(seed => seed.SeedAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("RUN_SEEDERS")]
    [InlineData("SEEDERS_ENABLED")]
    [InlineData("AUTH_SEED_USERS")]
    public async Task RunAsync_RunsSeeders_InNonDevelopment_WhenExplicitFlagIsSet(string flagKey)
    {
        var seeder = new Mock<ISeeder>();
        var orchestrator = CreateOrchestrator(
            [seeder.Object],
            environmentName: Environments.Production,
            configValues: new Dictionary<string, string?> { [flagKey] = "true" }
        );

        await orchestrator.RunAsync();

        seeder.Verify(seed => seed.SeedAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static SeederOrchestrator CreateOrchestrator(
        IEnumerable<ISeeder> seeders,
        string environmentName,
        IDictionary<string, string?>? configValues = null
    )
    {
        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(environment => environment.EnvironmentName)
            .Returns(environmentName);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?>())
            .Build();

        return new SeederOrchestrator(
            seeders,
            hostEnvironment.Object,
            configuration,
            Mock.Of<ILogger<SeederOrchestrator>>()
        );
    }
}
