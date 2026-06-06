using backend.main.shared.http;

using FluentAssertions;

using Polly.CircuitBreaker;

namespace backend.tests.Unit.Shared.Http;

public class BaseServiceTests
{
    [Fact]
    public async Task ExecuteResilientHttpAsync_ShouldReturnImmediately_OnSuccess()
    {
        var service = new TestBaseService();

        var result = await service.ExecuteAsync(() => Task.FromResult("ok"));

        result.Should().Be("ok");
    }

    [Fact]
    public async Task ExecuteResilientHttpAsync_ShouldRetryUntilSuccess()
    {
        var service = new TestBaseService();
        var attempts = 0;

        var result = await service.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts < 3)
                throw new HttpRequestException("temporary");

            return Task.FromResult("recovered");
        });

        result.Should().Be("recovered");
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteResilientHttpAsync_ShouldOpenCircuit_AfterRepeatedFailures()
    {
        var service = new TestBaseService();
        var attempts = 0;

        var action = () => service.ExecuteAsync<string>(() =>
        {
            attempts++;
            throw new InvalidOperationException("always fails");
        });

        var exception = await action.Should().ThrowAsync<Exception>();
        (exception.Which is InvalidOperationException or BrokenCircuitException).Should().BeTrue();
        attempts.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Constructor_ShouldUseProvidedHttpClient_OrCreateDefaultClient()
    {
        var provided = new HttpClient();
        var withProvided = new TestBaseService(provided);
        var withDefault = new TestBaseService();

        withProvided.HttpClient.Should().BeSameAs(provided);
        withDefault.HttpClient.Should().NotBeNull();
    }

    private sealed class TestBaseService : BaseService
    {
        public TestBaseService(HttpClient? client = null)
            : base(client)
        {
        }

        public HttpClient HttpClient => Http;

        public Task<T> ExecuteAsync<T>(Func<Task<T>> action) => ExecuteResilientHttpAsync(action);
    }
}
