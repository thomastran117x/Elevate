using System.Diagnostics.Metrics;

using backend.main.features.auth.events;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace backend.tests.Unit.Features.Auth.Events;

internal sealed class RecordingAuthSecurityEventSink : IAuthSecurityEventSink
{
    public List<AuthSecurityEvent> Events { get; } = [];

    public void Write(AuthSecurityEvent securityEvent) => Events.Add(securityEvent);
}

/// <summary>Captures events as the emitting code classified them, before enrichment.</summary>
internal sealed class RecordingAuthSecurityEventEmitter : IAuthSecurityEventEmitter
{
    public List<(AuthSecurityEvent Event, AuthSecurityTarget? Target)> Emitted { get; } = [];

    public IEnumerable<AuthSecurityEvent> Events => Emitted.Select(entry => entry.Event);

    public void Emit(AuthSecurityEvent securityEvent, AuthSecurityTarget? target = null) =>
        Emitted.Add((securityEvent, target));

    public IEnumerable<AuthSecurityEvent> Of(AuthSecurityEventDefinition definition) =>
        Events.Where(securityEvent => securityEvent.Definition == definition);
}

internal sealed class ThrowingAuthSecurityEventSink : IAuthSecurityEventSink
{
    public void Write(AuthSecurityEvent securityEvent) =>
        throw new InvalidOperationException("sink exploded with secret-in-message");
}

internal sealed record CapturedLog(LogLevel Level, EventId EventId, string Message, IReadOnlyList<KeyValuePair<string, object?>> State);

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<CapturedLog> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
        Entries.Add(new CapturedLog(logLevel, eventId, formatter(state, exception), properties.ToList()));
    }
}

/// <summary>Collects every measurement recorded on one <see cref="AuthSecurityMetrics"/> instance.</summary>
internal sealed class AuthMetricsRecorder : IDisposable
{
    private readonly MeterListener _listener = new();

    public AuthMetricsRecorder(IMeterFactory meterFactory)
    {
        // The factory caches meters, so this is the same instance AuthSecurityMetrics records on;
        // filtering on it keeps measurements from tests running in parallel out.
        var meter = meterFactory.Create(AuthSecurityMetrics.MeterName);
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.Start();
    }

    public List<(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags)> Measurements { get; } = [];

    public void Dispose() => _listener.Dispose();

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>();
        foreach (var tag in tags)
            copy[tag.Key] = tag.Value;
        lock (Measurements)
            Measurements.Add((instrument.Name, value, copy));
    }
}

internal static class AuthSecurityEventTestSupport
{
    public const string HashKey = "dW5pdC10ZXN0LWF1dGgtc2VjdXJpdHktZXZlbnQtaGFzaC1rZXk=";

    public static IMeterFactory CreateMeterFactory() =>
        new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    public static AuthSecurityEventsOptions Options(string? hashKey = HashKey) => new()
    {
        IdentifierHashKey = hashKey,
    };

    public static AuthIdentifierHasher Hasher(string? hashKey = HashKey) =>
        new(Microsoft.Extensions.Options.Options.Create(Options(hashKey)));

    public static AuthSecurityEventEmitter CreateEmitter(
        HttpContext? context,
        IEnumerable<IAuthSecurityEventSink> sinks,
        AuthSecurityMetrics? metrics = null,
        AuthSecurityEventsOptions? options = null,
        ILogger<AuthSecurityEventEmitter>? logger = null,
        TimeProvider? timeProvider = null)
    {
        options ??= Options();
        return new AuthSecurityEventEmitter(
            sinks,
            new AuthIdentifierHasher(Microsoft.Extensions.Options.Options.Create(options)),
            Monitor(options),
            new HttpContextAccessor { HttpContext = context },
            timeProvider ?? TimeProvider.System,
            metrics ?? new AuthSecurityMetrics(CreateMeterFactory()),
            logger ?? NullLogger<AuthSecurityEventEmitter>.Instance);
    }

    public static IOptionsMonitor<AuthSecurityEventsOptions> Monitor(AuthSecurityEventsOptions? options = null) =>
        new StaticOptionsMonitor(options ?? Options());

    private sealed class StaticOptionsMonitor(AuthSecurityEventsOptions value)
        : IOptionsMonitor<AuthSecurityEventsOptions>
    {
        public AuthSecurityEventsOptions CurrentValue => value;

        public AuthSecurityEventsOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AuthSecurityEventsOptions, string?> listener) => null;
    }
}
