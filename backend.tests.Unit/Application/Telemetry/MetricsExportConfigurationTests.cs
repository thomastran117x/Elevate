using backend.main.application.telemetry;
using backend.main.features.auth.events;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry.Metrics;

namespace backend.tests.Unit.Application.Telemetry;

public class MetricsExportConfigurationTests
{
    [Fact]
    public void AddMetricsExport_ShouldRegisterNothing_WhenNoOtlpEndpointIsConfigured()
    {
        var services = new ServiceCollection();

        services.AddMetricsExport(Configuration(), "eventxperience-api", AuthSecurityMetrics.MeterName);

        services.Should().BeEmpty("a deployment that sets no endpoint must behave exactly as before");
        using var provider = services.BuildServiceProvider();
        provider.GetService<MeterProvider>().Should().BeNull();
    }

    [Theory]
    [InlineData(MetricsExportConfiguration.OtlpEndpointKey)]
    [InlineData(MetricsExportConfiguration.OtlpMetricsEndpointKey)]
    public void AddMetricsExport_ShouldRegisterAnOtlpMeterProvider_WhenAnEndpointIsConfigured(string key)
    {
        var configuration = Configuration((key, "http://prometheus:9090/api/v1/otlp"));
        var services = new ServiceCollection().AddSingleton(configuration).AddLogging();

        services.AddMetricsExport(configuration, "eventxperience-api", AuthSecurityMetrics.MeterName);

        using var provider = services.BuildServiceProvider();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void IsEnabled_ShouldIgnoreBlankEndpoints()
    {
        MetricsExportConfiguration.IsEnabled(Configuration((MetricsExportConfiguration.OtlpEndpointKey, " "))).Should().BeFalse();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
}
