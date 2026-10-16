using backend.main.shared.exceptions.http;
using backend.main.shared.http;

using FluentAssertions;

using Google.Apis.Auth;

using Microsoft.IdentityModel.Tokens;

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
            throw new HttpRequestException("always fails");
        });

        var exception = await action.Should().ThrowAsync<Exception>();
        (exception.Which is HttpRequestException or BrokenCircuitException).Should().BeTrue();
        attempts.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task ExecuteResilientHttpAsync_ShouldNotRetryADeterministicRejection()
    {
        // An invalid token fails the same way every time; retrying it only holds the request open.
        var service = new TestBaseService();
        var attempts = 0;

        var action = () => service.ExecuteAsync<string>(() =>
        {
            attempts++;
            throw new InvalidJwtException("bad token");
        });

        await action.Should().ThrowAsync<InvalidJwtException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public void IsTransient_ShouldOnlyAcceptNetworkFaultsAndTimeouts()
    {
        BaseService.IsTransient(new HttpRequestException("network")).Should().BeTrue();
        BaseService.IsTransient(new IOException("connection reset")).Should().BeTrue();
        BaseService.IsTransient(new TaskCanceledException("timeout", new TimeoutException())).Should().BeTrue();
        BaseService.IsTransient(new InvalidJwtException("bad token")).Should().BeFalse();
        BaseService.IsTransient(new SecurityTokenInvalidSignatureException("bad signature")).Should().BeFalse();
        BaseService.IsTransient(new UnauthorizedException("refused")).Should().BeFalse();
        BaseService.IsTransient(new TaskCanceledException("caller cancelled")).Should().BeFalse();
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
