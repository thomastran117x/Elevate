using System.Text.Json;
using System.Text.RegularExpressions;

using backend.main.features.auth.events;
using backend.main.shared.providers.messages;
using backend.main.shared.providers.messaging;

using FluentAssertions;

using YamlDotNet.RepresentationModel;

namespace backend.tests.Unit.Application.Telemetry;

/// <summary>
/// Keeps the monitoring definitions in docker/monitoring honest against the code and the
/// operations docs. It checks:
/// <list type="bullet">
/// <item>every alert has an owner, severity, mode, rationale, and a runbook heading that exists;</item>
/// <item>every alert has firing and silent promtool cases;</item>
/// <item>rules and the dashboard only match catalog events and classifications;</item>
/// <item>nothing groups by, or labels with, a value that could identify a person.</item>
/// </list>
/// promtool itself (CI's Monitoring Definitions job) checks that the PromQL is valid and behaves.
/// </summary>
public partial class MonitoringDefinitionsTests
{
    private const string RepositoryBlobPrefix = "https://github.com/thomastran117x/Elevate/blob/main/";
    private const string DashboardUid = "auth-security";

    private static readonly string[] Severities = ["page", "ticket"];
    private static readonly string[] Modes = ["observe", "page"];
    private static readonly string[] RequiredAnnotations = ["summary", "description", "runbook_url", "dashboard_uid", "threshold_rationale"];

    // The areas #330 requires the dashboard to cover, as row titles.
    private static readonly string[] RequiredDashboardRows =
    [
        "Sign-in outcomes",
        "Account and IP throttles",
        "Recovery and MFA delivery",
        "Step-up and verification failures",
        "Token reuse and session revocation",
        "Provider linking",
        "Authorization denials",
        "Latency and faults",
        "Pipeline health",
        "Alert history",
    ];

    // Labels a rule may group by or a panel may show. All come from closed sets; anything that
    // could carry an account, session, hash, address, route, or correlation id is absent.
    private static readonly HashSet<string> AllowedLabels =
    [
        "deployment_environment_name", "job", "event", "outcome", "reason", "method", "flow", "dimension",
        "factor", "operation", "sink", "message_type", "le", "alertname", "alertstate", "mode", "severity", "owner",
    ];

    // Go template actions in annotations, which are not labels.
    private static readonly HashSet<string> TemplateKeywords = ["else", "end", "with", "if", "range"];

    private static readonly Lazy<IReadOnlyList<AlertRule>> Alerts = new(LoadAlerts);
    private static readonly Lazy<JsonDocument> Dashboard = new(() =>
        JsonDocument.Parse(File.ReadAllText(MonitoringPath("grafana", "dashboards", "auth-security.json"))));

    [Fact]
    public void Alerts_ShouldBeDefined_WithUniqueNames()
    {
        Alerts.Value.Should().HaveCountGreaterThanOrEqualTo(10);
        Alerts.Value.Select(alert => alert.Name).Should().OnlyHaveUniqueItems();
        Alerts.Value.Should().OnlyContain(alert => alert.Name.StartsWith("Auth", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryAlert_ShouldDeclareSeverityOwnerModeAndItsAnnotations()
    {
        var owners = OwnerRoleIds();

        foreach (var alert in Alerts.Value)
        {
            alert.Labels.Should().ContainKey("severity").WhoseValue.Should().BeOneOf(Severities, alert.Name);
            alert.Labels.Should().ContainKey("mode").WhoseValue.Should().BeOneOf(Modes, alert.Name);
            alert.Labels.Should().ContainKey("owner").WhoseValue.Should().BeOneOf(owners, $"{alert.Name}'s owner must be a role in ownership.md");
            foreach (var annotation in RequiredAnnotations)
                alert.Annotations.Should().ContainKey(annotation).WhoseValue.Should().NotBeNullOrWhiteSpace($"{alert.Name} needs {annotation}");
            alert.Annotations["dashboard_uid"].Should().Be(DashboardUid, alert.Name);
        }
    }

    [Fact]
    public void EveryAlert_ShouldStartInObserveMode_UntilItsBaselineHasBeenReviewed()
    {
        // Flip an alert to mode="page" in its own reviewed change once its two-week baseline is
        // recorded (docs/operations/monitoring.md#rollout), and list it here in that change.
        string[] reviewedForPaging = [];

        Alerts.Value
            .Where(alert => alert.Labels["mode"] == "page")
            .Select(alert => alert.Name)
            .Should().BeSubsetOf(reviewedForPaging);
    }

    [Fact]
    public void EveryRunbookLink_ShouldResolveToAHeadingInTheRepository()
    {
        foreach (var alert in Alerts.Value)
        {
            var url = alert.Annotations["runbook_url"];
            url.Should().StartWith(RepositoryBlobPrefix + "docs/operations/runbooks/", alert.Name);

            var (path, anchor) = SplitAnchor(url[RepositoryBlobPrefix.Length..]);
            var file = Path.Combine(AppContext.BaseDirectory, "Docs", "operations", path["docs/operations/".Length..]);
            File.Exists(file).Should().BeTrue($"{alert.Name} links to {path}");
            anchor.Should().Be(alert.Name.ToLowerInvariant(), "each alert has its own runbook section");
            HeadingAnchors(file).Should().Contain(anchor, $"{path} needs a heading for {alert.Name}");
        }
    }

    [Fact]
    public void EveryAlert_ShouldBeListedInTheMonitoringGuide()
    {
        var guide = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Docs", "operations", "monitoring.md"));

        foreach (var alert in Alerts.Value)
            guide.Should().Contain($"| `{alert.Name}` |", "the alert table in monitoring.md lists every alert");
    }

    [Fact]
    public void EveryAlert_ShouldHaveFiringAndSilentPromtoolCases()
    {
        var cases = LoadPromtoolAlertChecks();

        foreach (var alert in Alerts.Value)
        {
            cases.Should().Contain(check => check.Alert == alert.Name && check.Fires, $"{alert.Name} needs a case where it fires");
            cases.Should().Contain(check => check.Alert == alert.Name && !check.Fires, $"{alert.Name} needs a case where it stays silent");
        }
    }

    [Fact]
    public void RulesAndDashboard_ShouldOnlyMatchCatalogEventsAndClassifications()
    {
        var allowed = AllowedMatcherValues();
        var problems = new List<string>();

        foreach (var expression in AllExpressions())
        {
            foreach (Match matcher in LabelMatcher().Matches(expression))
            {
                var label = matcher.Groups["label"].Value;
                var op = matcher.Groups["op"].Value;
                var value = matcher.Groups["value"].Value;
                if (!allowed.TryGetValue(label, out var values))
                    continue;

                var alternatives = op is "=~" or "!~" ? value.Split('|') : [value];
                foreach (var alternative in alternatives.Where(alternative => alternative.Length > 0))
                {
                    var known = alternative.Contains('*') || alternative.Contains('+')
                        ? values.Any(candidate => Regex.IsMatch(candidate, $"^(?:{alternative})$"))
                        : values.Contains(alternative);
                    if (!known)
                        problems.Add($"{label}{op}\"{alternative}\" in: {expression}");
                }
            }
        }

        problems.Should().BeEmpty("rules and panels may only match values the code can emit");
    }

    [Fact]
    public void RulesAndDashboard_ShouldNeverGroupByOrDisplayIdentifyingLabels()
    {
        var used = new List<string>();
        foreach (var expression in AllExpressions())
        {
            foreach (Match grouping in Grouping().Matches(expression))
            {
                used.AddRange(grouping.Groups["labels"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            foreach (Match matcher in AnyLabelMatcher().Matches(expression))
                used.Add(matcher.Groups["label"].Value);
        }

        foreach (var template in DashboardLegends().Concat(Alerts.Value.SelectMany(alert => alert.Annotations.Values)))
        {
            foreach (Match label in TemplateLabel().Matches(template))
            {
                if (!TemplateKeywords.Contains(label.Groups["label"].Value))
                    used.Add(label.Groups["label"].Value);
            }
        }

        used.Where(label => label != "__name__").Distinct().Should().BeSubsetOf(
            AllowedLabels,
            "monitoring holds aggregates only; identifiers stay in the event log");
    }

    [Fact]
    public void Dashboard_ShouldCoverEveryRequiredArea()
    {
        var root = Dashboard.Value.RootElement;
        root.GetProperty("uid").GetString().Should().Be(DashboardUid);

        var panels = root.GetProperty("panels").EnumerateArray().ToArray();
        var rows = panels.Where(panel => panel.GetProperty("type").GetString() == "row")
            .Select(panel => panel.GetProperty("title").GetString())
            .ToArray();
        rows.Should().BeEquivalentTo(RequiredDashboardRows);

        // Every row has at least one panel after it, before the next row.
        for (var index = 0; index < panels.Length; index++)
        {
            if (panels[index].GetProperty("type").GetString() != "row")
                continue;

            var next = index + 1 < panels.Length ? panels[index + 1].GetProperty("type").GetString() : null;
            next.Should().NotBeNull().And.NotBe("row", $"row '{panels[index].GetProperty("title").GetString()}' needs a panel");
        }
    }

    [Fact]
    public void DashboardPanels_ShouldUseTheProvisionedDataSourceAndTheEnvironmentFilter()
    {
        foreach (var panel in DataPanels())
        {
            panel.GetProperty("datasource").GetProperty("uid").GetString().Should().Be("prometheus");
            foreach (var target in panel.GetProperty("targets").EnumerateArray())
            {
                var expr = target.GetProperty("expr").GetString()!;
                if (!expr.Contains("ALERTS", StringComparison.Ordinal))
                    expr.Should().Contain("deployment_environment_name=~\"$env\"", panel.GetProperty("title").GetString());
            }
        }
    }

    private static IEnumerable<string> AllExpressions()
    {
        foreach (var alert in Alerts.Value)
            yield return alert.Expression;

        foreach (var expression in RecordingExpressions())
            yield return expression;

        foreach (var panel in DataPanels())
        {
            foreach (var target in panel.GetProperty("targets").EnumerateArray())
                yield return target.GetProperty("expr").GetString()!;
        }
    }

    private static IEnumerable<JsonElement> DataPanels() =>
        Dashboard.Value.RootElement.GetProperty("panels").EnumerateArray()
            .Where(panel => panel.GetProperty("type").GetString() != "row");

    private static IEnumerable<string> DashboardLegends() =>
        DataPanels()
            .SelectMany(panel => panel.GetProperty("targets").EnumerateArray())
            .Where(target => target.TryGetProperty("legendFormat", out _))
            .Select(target => target.GetProperty("legendFormat").GetString()!);

    private static Dictionary<string, HashSet<string>> AllowedMatcherValues()
    {
        static HashSet<string> Wire<TEnum>() where TEnum : struct, Enum =>
            Enum.GetValues<TEnum>().Select(value => AuthSecurityWireNames.Of(value)).ToHashSet(StringComparer.Ordinal);

        var outcomes = Wire<AuthSecurityOutcome>();
        outcomes.UnionWith(Enum.GetValues<NotificationDeliveryOutcome>().Select(NotificationDeliveryMetrics.WireName));

        return new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["event"] = AuthSecurityEvents.All.Select(definition => definition.Name).ToHashSet(StringComparer.Ordinal),
            ["outcome"] = outcomes,
            ["reason"] = Wire<AuthSecurityReason>(),
            ["method"] = Wire<AuthSecurityMethod>(),
            ["flow"] = Wire<AuthSecurityFlow>(),
            ["dimension"] = Wire<AuthThrottleDimension>(),
            ["factor"] = Wire<AuthSecurityFactor>(),
            ["operation"] = Wire<AuthOperation>(),
            ["message_type"] = Enum.GetValues<EmailMessageType>().Select(NotificationDeliveryMetrics.WireName).ToHashSet(StringComparer.Ordinal),
        };
    }

    private static string[] OwnerRoleIds()
    {
        var ownership = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Docs", "operations", "ownership.md"));
        return ownership
            .Select(line => OwnerRow().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["id"].Value)
            .ToArray();
    }

    private static IReadOnlyList<AlertRule> LoadAlerts()
    {
        var root = LoadYaml(MonitoringPath("prometheus", "rules", "auth-alerts.yml"));
        var alerts = new List<AlertRule>();

        foreach (var group in Sequence(root, "groups"))
        {
            foreach (var rule in Sequence((YamlMappingNode)group, "rules").Cast<YamlMappingNode>())
            {
                if (!rule.Children.ContainsKey("alert"))
                    continue;

                alerts.Add(new AlertRule(
                    Scalar(rule, "alert"),
                    Scalar(rule, "expr"),
                    Mapping(rule, "labels"),
                    Mapping(rule, "annotations")));
            }
        }

        return alerts;
    }

    private static IEnumerable<string> RecordingExpressions()
    {
        var root = LoadYaml(MonitoringPath("prometheus", "rules", "auth-recording.yml"));
        return Sequence(root, "groups")
            .SelectMany(group => Sequence((YamlMappingNode)group, "rules").Cast<YamlMappingNode>())
            .Select(rule => Scalar(rule, "expr"));
    }

    private static List<PromtoolCheck> LoadPromtoolAlertChecks()
    {
        var root = LoadYaml(MonitoringPath("prometheus", "tests", "auth-alerts.test.yml"));
        var checks = new List<PromtoolCheck>();

        foreach (var test in Sequence(root, "tests").Cast<YamlMappingNode>())
        {
            if (!test.Children.TryGetValue(new YamlScalarNode("promql_expr_test"), out var expressions))
                continue;

            foreach (var check in ((YamlSequenceNode)expressions).Cast<YamlMappingNode>())
            {
                var alert = AlertSelector().Match(Scalar(check, "expr"));
                if (!alert.Success)
                    continue;

                var fires = check.Children.TryGetValue(new YamlScalarNode("exp_samples"), out var samples)
                    && samples is YamlSequenceNode { Children.Count: > 0 };
                checks.Add(new PromtoolCheck(alert.Groups["name"].Value, fires));
            }
        }

        return checks;
    }

    private static HashSet<string> HeadingAnchors(string markdownFile) =>
        File.ReadLines(markdownFile)
            .Where(line => line.StartsWith('#'))
            .Select(line => GitHubAnchor(line.TrimStart('#').Trim()))
            .ToHashSet(StringComparer.Ordinal);

    // GitHub's heading slug: lowercase, punctuation other than '-' and '_' dropped, spaces to '-'.
    private static string GitHubAnchor(string heading) =>
        AnchorPunctuation().Replace(heading.ToLowerInvariant(), string.Empty).Replace(' ', '-');

    private static (string Path, string Anchor) SplitAnchor(string target)
    {
        var hash = target.IndexOf('#');
        return hash < 0 ? (target, string.Empty) : (target[..hash], target[(hash + 1)..]);
    }

    private static string MonitoringPath(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "Monitoring", .. parts]);

    private static YamlMappingNode LoadYaml(string path)
    {
        var stream = new YamlStream();
        using var reader = new StreamReader(path);
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static YamlSequenceNode Sequence(YamlMappingNode node, string key) =>
        (YamlSequenceNode)node.Children[new YamlScalarNode(key)];

    private static string Scalar(YamlMappingNode node, string key) =>
        ((YamlScalarNode)node.Children[new YamlScalarNode(key)]).Value ?? string.Empty;

    private static Dictionary<string, string> Mapping(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlMappingNode mapping
            ? mapping.Children.ToDictionary(
                pair => ((YamlScalarNode)pair.Key).Value!,
                pair => ((YamlScalarNode)pair.Value).Value ?? string.Empty,
                StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    [GeneratedRegex(@"(?<label>[a-z_]+)\s*(?<op>=~|!~|!=|=)\s*""(?<value>[^""]*)""")]
    private static partial Regex LabelMatcher();

    [GeneratedRegex(@"(?<label>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=~|!~|!=|=)\s*""")]
    private static partial Regex AnyLabelMatcher();

    [GeneratedRegex(@"\b(?:by|without|on|ignoring|group_left|group_right)\s*\((?<labels>[^)]*)\)")]
    private static partial Regex Grouping();

    [GeneratedRegex(@"\{\{\s*(?:\$labels\.)?(?<label>[A-Za-z_][A-Za-z0-9_]*)\s*\}\}")]
    private static partial Regex TemplateLabel();

    [GeneratedRegex(@"alertname=""(?<name>[A-Za-z]+)""")]
    private static partial Regex AlertSelector();

    [GeneratedRegex(@"^\|\s*`(?<id>[a-z-]+)`\s*\|")]
    private static partial Regex OwnerRow();

    [GeneratedRegex(@"[^a-z0-9 _-]")]
    private static partial Regex AnchorPunctuation();

    private sealed record AlertRule(
        string Name,
        string Expression,
        Dictionary<string, string> Labels,
        Dictionary<string, string> Annotations);

    private sealed record PromtoolCheck(string Alert, bool Fires);
}
