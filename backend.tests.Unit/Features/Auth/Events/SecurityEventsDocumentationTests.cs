using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;

using backend.main.features.auth.events;

using FluentAssertions;

using Microsoft.Extensions.Logging;

namespace backend.tests.Unit.Features.Auth.Events;

/// <summary>
/// Keeps <c>docs/SECURITY_EVENTS.md</c> in step with the contract it documents: the catalog, the
/// reason codes, the operations, and the metric instruments and their tags.
/// </summary>
public sealed partial class SecurityEventsDocumentationTests
{
    private static readonly string[] Document = File.ReadAllLines(
        Path.Combine(AppContext.BaseDirectory, "Docs", "SECURITY_EVENTS.md"));

    [Fact]
    public void CatalogTable_ShouldMatchTheEventCatalog()
    {
        var rows = Document
            .Select(line => CatalogRow().Match(line))
            .Where(match => match.Success)
            .Select(match => (
                Id: int.Parse(match.Groups["id"].Value),
                Name: match.Groups["name"].Value,
                Level: match.Groups["level"].Value))
            .ToList();

        rows.Should().Equal(AuthSecurityEvents.All.Select(definition =>
            (definition.Id, definition.Name, definition.Level.ToString())));
    }

    [Fact]
    public void CatalogTable_ShouldOnlyListOutcomesAndReasonsThatExist()
    {
        var outcomes = WireNames<AuthSecurityOutcome>();
        var reasons = WireNames<AuthSecurityReason>();

        foreach (var line in Document.Where(line => CatalogRow().IsMatch(line)))
        {
            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            Codes(cells[5]).Should().BeSubsetOf(outcomes, line);
            Codes(cells[6]).Should().BeSubsetOf(reasons, line);
        }
    }

    [Fact]
    public void ReasonTable_ShouldDocumentEveryReasonExactlyOnce()
    {
        var documented = Section("### Reason codes")
            .Select(line => ReasonRow().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["reason"].Value)
            .ToList();

        documented.Should().OnlyHaveUniqueItems();
        documented.Should().BeEquivalentTo(WireNames<AuthSecurityReason>());
    }

    [Fact]
    public void Operations_ShouldMatchTheTimedOperations()
    {
        var line = Document.Single(text => text.StartsWith("`operation` is one of", StringComparison.Ordinal));
        var documented = Codes(line[..line.IndexOf("Its `outcome`", StringComparison.Ordinal)])
            .Where(code => code != "operation");

        documented.Should().BeEquivalentTo(WireNames<AuthOperation>());
    }

    [Fact]
    public void MetricsTable_ShouldMatchTheInstrumentsAndTheirTags()
    {
        var documented = Section("## Metrics")
            .Select(line => MetricRow().Match(line))
            .Where(match => match.Success)
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => Codes(match.Groups["tags"].Value).ToHashSet());

        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        using var recorder = new AuthMetricsRecorder(meterFactory);
        var metrics = new AuthSecurityMetrics(meterFactory);
        metrics.RecordEvent(new AuthSecurityEvent
        {
            Definition = AuthSecurityEvents.SignInFailed,
            Outcome = AuthSecurityOutcome.Denied,
        });
        metrics.RecordSinkFailure("ExampleSink");
        metrics.RecordSecurityHistoryPublishFailure();
        metrics.RecordOperation(AuthOperation.SignIn, AuthSecurityOutcome.Succeeded, TimeSpan.FromMilliseconds(1));
        var observed = recorder.Measurements.ToDictionary(
            measurement => measurement.Instrument,
            measurement => measurement.Tags.Keys.ToHashSet());

        documented.Keys.Should().BeEquivalentTo(observed.Keys);
        foreach (var (instrument, tags) in observed)
            documented[instrument].Should().BeEquivalentTo(tags, instrument);
    }

    [Fact]
    public void MeterName_ShouldBeTheOneDocumented()
    {
        Document.Should().Contain(line => line.Contains($"`{AuthSecurityMetrics.MeterName}` meter", StringComparison.Ordinal));
    }

    private static IEnumerable<string> Section(string heading) =>
        Document
            .SkipWhile(line => line != heading)
            .Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal)
                && !line.StartsWith("### ", StringComparison.Ordinal));

    private static IEnumerable<string> Codes(string text) =>
        CodeSpan().Matches(text).Select(match => match.Groups[1].Value);

    private static IReadOnlyCollection<string> WireNames<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(value => AuthSecurityWireNames.Of(value)).ToList();

    [GeneratedRegex(@"^\| (?<id>\d{5}) \| `(?<name>[a-z_.]+)` \| (?<level>[A-Za-z]+) \|")]
    private static partial Regex CatalogRow();

    [GeneratedRegex(@"^\| `(?<reason>[a-z_]+)` \|")]
    private static partial Regex ReasonRow();

    [GeneratedRegex(@"^\| `(?<name>auth\.[a-z_.]+)` \| [A-Za-z]+ \| `[^`]+` \| (?<tags>.+) \|$")]
    private static partial Regex MetricRow();

    [GeneratedRegex("`([a-z_.]+)`")]
    private static partial Regex CodeSpan();
}
