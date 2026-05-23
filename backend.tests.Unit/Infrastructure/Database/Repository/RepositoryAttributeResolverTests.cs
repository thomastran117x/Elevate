using System.Reflection;

using backend.main.infrastructure.database.repository;
using backend.main.shared.attributes.repository;

using FluentAssertions;

using Microsoft.Extensions.Logging;

namespace backend.tests.Unit.Infrastructure.Database.Repository;

public class RepositoryAttributeResolverTests
{
    [Fact]
    public void GetBehavior_ShouldHonorPrecedenceAndWarnOnConflictingRetryAttributes()
    {
        var logger = new ListLogger<RepositoryAttributeResolver>();
        var resolver = new RepositoryAttributeResolver(logger);

        var behavior = resolver.GetBehavior(
            typeof(IConflictingRepository).GetMethod(nameof(IConflictingRepository.SaveAsync))!,
            typeof(ConflictingRepository).GetMethod(nameof(ConflictingRepository.SaveAsync))!,
            typeof(ConflictingRepository));

        behavior.NoRetry.Should().BeTrue();
        behavior.HandleMissingEntity.Should().BeTrue();
        logger.Messages.Should().ContainSingle(message => message.Contains("Conflicting repository attributes"));
    }

    [Fact]
    public void GetBehavior_ShouldUseImplementationClassAndInterfaceTypeFallbacks()
    {
        var resolver = new RepositoryAttributeResolver();

        var behavior = resolver.GetBehavior(
            typeof(ITypeLevelMissingRepository).GetMethod(nameof(ITypeLevelMissingRepository.DeleteAsync))!,
            typeof(TypeLevelMissingRepository).GetMethod(nameof(TypeLevelMissingRepository.DeleteAsync))!,
            typeof(TypeLevelMissingRepository));

        behavior.NoRetry.Should().BeTrue();
        behavior.HandleMissingEntity.Should().BeTrue();
    }

    [RetryOnTransientFailure]
    public interface IConflictingRepository
    {
        [NoRetry]
        [HandleMissingEntity]
        Task SaveAsync();
    }

    [RetryOnTransientFailure]
    private sealed class ConflictingRepository : IConflictingRepository
    {
        public Task SaveAsync() => Task.CompletedTask;
    }

    [NoRetry]
    [HandleMissingEntity]
    public interface ITypeLevelMissingRepository
    {
        Task DeleteAsync();
    }

    private sealed class TypeLevelMissingRepository : ITypeLevelMissingRepository
    {
        public Task DeleteAsync() => Task.CompletedTask;
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
