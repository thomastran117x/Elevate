using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace backend.main.application.telemetry
{
    /// <summary>
    /// Pushes the named meters over OTLP when an OTLP endpoint is configured, and does nothing
    /// otherwise. The API and the workers export the same way, so a deployment that sets no
    /// endpoint behaves exactly as before and no metrics endpoint is ever exposed on the API.
    /// </summary>
    /// <remarks>
    /// Configuration uses the standard OpenTelemetry environment variables, read through
    /// <see cref="IConfiguration"/>: <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (or
    /// <c>OTEL_EXPORTER_OTLP_METRICS_ENDPOINT</c>), <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>,
    /// <c>OTEL_METRIC_EXPORT_INTERVAL</c>, and <c>OTEL_RESOURCE_ATTRIBUTES</c>. The monitoring
    /// stack in docker/monitoring receives them with Prometheus's OTLP receiver. Only meters
    /// whose tags come from closed sets are exported; see docs/SECURITY_EVENTS.md#metrics.
    /// </remarks>
    public static class MetricsExportConfiguration
    {
        public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
        public const string OtlpMetricsEndpointKey = "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT";

        public static bool IsEnabled(IConfiguration configuration) =>
            !string.IsNullOrWhiteSpace(configuration[OtlpEndpointKey])
            || !string.IsNullOrWhiteSpace(configuration[OtlpMetricsEndpointKey]);

        public static IServiceCollection AddMetricsExport(
            this IServiceCollection services,
            IConfiguration configuration,
            string serviceName,
            params string[] meterNames)
        {
            if (!IsEnabled(configuration))
                return services;

            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService(serviceName))
                .WithMetrics(metrics =>
                {
                    metrics.AddMeter(meterNames);
                    metrics.AddOtlpExporter();
                });

            return services;
        }
    }
}
