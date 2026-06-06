using System.Data.Common;
using System.Reflection;

using backend.main.infrastructure.database.repository;
using backend.main.shared.exceptions.app;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Polly;

namespace backend.tests.Unit.Infrastructure.Database.Repository;

public class RepositoryResiliencePolicyTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnActionResult_ForSuccessfulCalls()
    {
        var policy = new RepositoryResiliencePolicy();

        var result = await policy.ExecuteAsync(_ => Task.FromResult("ok"), "read");

        result.Should().Be("ok");
        policy.IsDatabaseHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTranslateDbUpdateException_ToRepositoryWriteException()
    {
        var policy = new RepositoryResiliencePolicy();

        var act = () => policy.ExecuteAsync<string>(
            _ => Task.FromException<string>(new DbUpdateException("outer", new Exception("inner"))),
            "write");

        await act.Should().ThrowAsync<RepositoryWriteException>()
            .WithMessage("inner");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTranslateTimeoutRejectedException_ToRepositoryTimeoutException()
    {
        var policy = new RepositoryResiliencePolicy();

        var act = () => policy.ExecuteAsync<string>(
            async ct =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return "late";
            },
            "timeout");

        await act.Should().ThrowAsync<RepositoryTimeoutException>();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTranslateBrokenCircuitException_ToRepositoryUnavailableException()
    {
        var policy = new RepositoryResiliencePolicy();
        var breaker = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(1, TimeSpan.FromMinutes(1));

        try
        {
            await breaker.ExecuteAsync(_ => Task.FromException(new TimeoutException("boom")), CancellationToken.None);
        }
        catch (TimeoutException)
        {
            // expected: first failure opens the circuit
        }

        SetPolicyField(policy, "_policy", breaker);
        SetPolicyField(policy, "_circuitBreaker", breaker);

        var act = () => policy.ExecuteAsync(_ => Task.CompletedTask, "circuit");

        await act.Should().ThrowAsync<RepositoryUnavailableException>();
        policy.IsDatabaseHealthy.Should().BeFalse();
    }

    [Fact]
    public void IsTransient_ShouldHandleTimeoutCancellation_AndDbExceptions()
    {
        InvokeIsTransient(new OperationCanceledException()).Should().BeFalse();
        InvokeIsTransient(new TimeoutException("timeout")).Should().BeTrue();
        InvokeIsTransient(new TestDbException("HY000")).Should().BeTrue();
        InvokeIsTransient(new TestDbException("23000")).Should().BeFalse();
        InvokeIsTransient(new DbUpdateException("wrapped", new TestDbException("08001"))).Should().BeTrue();
        InvokeIsTransient(new DbUpdateException("wrapped", new TestDbException("42000"))).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogTransientRetryWarnings()
    {
        var logger = new ListLogger<RepositoryResiliencePolicy>();
        var policy = new RepositoryResiliencePolicy(logger);
        var attempts = 0;

        var act = () => policy.ExecuteAsync<string>(
            _ =>
            {
                attempts++;
                return Task.FromException<string>(new TimeoutException("transient"));
            },
            "retry");

        await act.Should().ThrowAsync<TimeoutException>();
        attempts.Should().Be(4);
        logger.Messages.Should().Contain(message =>
            message.Contains("Repository retry attempt"));
    }

    private static bool InvokeIsTransient(Exception exception)
    {
        var method = typeof(RepositoryResiliencePolicy).GetMethod(
            "IsTransient",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null, [exception])!;
    }

    private static void SetPolicyField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, value);
    }

    private sealed class TestDbException : DbException
    {
        private readonly string? _sqlState;

        public TestDbException(string? sqlState)
        {
            _sqlState = sqlState;
        }

        public override string? SqlState => _sqlState;
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
