using System.Net;

using backend.main.application.handlers;
using backend.main.shared.providers;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Hosting;

namespace backend.tests.Unit.Application.Handlers;

public class WebHandlerTests
{
    [Fact]
    public void AddWebConfiguration_ShouldRegisterTypedClient_WithConfiguredTimeout()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
            new PrimaryHandlerFilter(() => new SequenceHandler([new HttpResponseMessage(HttpStatusCode.OK)])));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalApi:TimeoutSeconds"] = "27",
                ["ExternalApi:Retry:MaxAttempts"] = "2",
                ["ExternalApi:Retry:BaseDelayMs"] = "1"
            })
            .Build();

        services.AddWebConfiguration(config);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IExternalApiClient>();

        client.Should().BeOfType<ExternalApiClient>();
        client.HttpClient.Timeout.Should().Be(TimeSpan.FromSeconds(27));
    }

    [Fact]
    public void AddWebConfiguration_ShouldUseDefaultTimeout_WhenConfigIsMissing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
            new PrimaryHandlerFilter(() => new SequenceHandler([new HttpResponseMessage(HttpStatusCode.OK)])));

        var config = new ConfigurationBuilder().Build();

        services.AddWebConfiguration(config);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IExternalApiClient>();

        client.HttpClient.Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AddWebConfiguration_ShouldRetryServerErrors()
    {
        var handler = new SequenceHandler(
        [
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.OK)
        ]);

        await using var provider = BuildProvider(handler, new Dictionary<string, string?>
        {
            ["ExternalApi:Retry:MaxAttempts"] = "2",
            ["ExternalApi:Retry:BaseDelayMs"] = "1"
        });

        var client = provider.GetRequiredService<IExternalApiClient>();

        var response = await client.GetAsync("https://service.test/ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task AddWebConfiguration_ShouldRetryTransientExceptions()
    {
        var handler = new SequenceHandler(
        [
            new HttpRequestException("offline"),
            new HttpResponseMessage(HttpStatusCode.OK)
        ]);

        await using var provider = BuildProvider(handler, new Dictionary<string, string?>
        {
            ["ExternalApi:Retry:MaxAttempts"] = "2",
            ["ExternalApi:Retry:BaseDelayMs"] = "1"
        });

        var client = provider.GetRequiredService<IExternalApiClient>();

        var response = await client.GetAsync("https://service.test/ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task AddWebConfiguration_ShouldNotRetryClientErrors()
    {
        var handler = new SequenceHandler([new HttpResponseMessage(HttpStatusCode.BadRequest)]);

        await using var provider = BuildProvider(handler, new Dictionary<string, string?>
        {
            ["ExternalApi:Retry:MaxAttempts"] = "3",
            ["ExternalApi:Retry:BaseDelayMs"] = "1"
        });

        var client = provider.GetRequiredService<IExternalApiClient>();

        var response = await client.GetAsync("https://service.test/ping");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        handler.CallCount.Should().Be(1);
    }

    private static ServiceProvider BuildProvider(SequenceHandler handler, Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
            new PrimaryHandlerFilter(() => handler));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        services.AddWebConfiguration(config);
        return services.BuildServiceProvider();
    }

    private sealed class PrimaryHandlerFilter(Func<HttpMessageHandler> factory) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
            builder =>
            {
                next(builder);
                builder.PrimaryHandler = factory();
            };
    }

    private sealed class SequenceHandler(IEnumerable<object> sequence) : HttpMessageHandler
    {
        private readonly Queue<object> _sequence = new(sequence);

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;

            var next = _sequence.Dequeue();
            if (next is Exception exception)
                throw exception;

            return Task.FromResult((HttpResponseMessage)next);
        }
    }
}
