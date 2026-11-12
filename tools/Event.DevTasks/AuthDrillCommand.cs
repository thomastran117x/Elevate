using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static partial class DevTasksCli
{
    private static readonly string[] DrillScenarios = ["session-revocation", "token-reuse", "recovery", "telemetry"];

    private const string DrillLimitsHint =
        "the drill makes more auth requests from one address than the default limits allow; raise "
        + "RateLimiter__AuthPermitLimit and AUTH_ABUSE_SHARED_IP_LIMIT for the drill environment, see docs/operations/drills.md";

    /// <summary>
    /// Runs the session-revocation, token-reuse, recovery, and telemetry drills against a running,
    /// non-production API. It uses a throwaway account whose email is caught by Mailpit, and
    /// writes an auditable JSON report.
    /// </summary>
    /// <remarks>
    /// The drill is HTTP-only and has no reference to the backend. It proves the controls from the
    /// outside, the way an attacker or a user meets them. The report records step outcomes,
    /// durations, and the API's <c>X-Request-Id</c> values. It never records the account's email,
    /// password, tokens, links, or codes, so it can be filed in the drill log.
    /// </remarks>
    private static async Task<int> RunAuthDrillAsync(string[] args)
    {
        var options = new AuthDrillOptions();

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--base-url":
                    options.BaseUrl = new Uri(ReadRequiredValue(args, ref index, "base URL"));
                    break;
                case "--mailpit-url":
                    options.MailpitUrl = new Uri(ReadRequiredValue(args, ref index, "Mailpit URL"));
                    break;
                case "--prometheus-url":
                    options.PrometheusUrl = new Uri(ReadRequiredValue(args, ref index, "Prometheus URL"));
                    break;
                case "--environment":
                    options.Environment = ReadRequiredValue(args, ref index, "environment");
                    break;
                case "--scenario":
                    options.Scenario = ReadRequiredValue(args, ref index, "scenario").ToLowerInvariant();
                    break;
                case "--report":
                    options.ReportPath = ReadRequiredValue(args, ref index, "report path");
                    break;
                case "--confirm-non-production":
                    options.ConfirmedNonProduction = true;
                    break;
                case var option when IsHelp(option):
                    WriteAuthDrillHelp();
                    return 0;
                default:
                    return Fail($"Unknown auth-drill option '{args[index]}'.");
            }
        }

        if (options.BaseUrl is null || options.MailpitUrl is null)
            return Fail("auth-drill needs --base-url and --mailpit-url.");
        if (options.Scenario != "all" && !DrillScenarios.Contains(options.Scenario))
            return Fail($"Unknown scenario '{options.Scenario}'. Use all or one of: {string.Join(", ", DrillScenarios)}.");
        if (options.Scenario == "telemetry" && options.PrometheusUrl is null)
            return Fail("The telemetry scenario needs --prometheus-url.");
        if (!options.ConfirmedNonProduction && !LooksNonProduction(options.BaseUrl))
        {
            return Fail(
                $"'{options.BaseUrl.Host}' does not look like a non-production host. The drill creates and deletes "
                + "an account and sends wrong passwords until throttled. Pass --confirm-non-production to run it anyway.");
        }

        var runId = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
            + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        var report = new DrillReport(runId, options.Environment, options.BaseUrl.GetLeftPart(UriPartial.Authority), DateTime.UtcNow);
        var drill = new AuthDrill(options, runId, report);

        try
        {
            await drill.RunAsync();
        }
        finally
        {
            report.FinishedAtUtc = DateTime.UtcNow;
            var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            var path = Path.GetFullPath(Path.Combine(repoRoot, options.ReportPath ?? Path.Combine(".tmp", "auth-drill", $"{runId}.json")));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, report.ToJson());
            Console.WriteLine();
            Console.WriteLine($"Drill {runId}: {report.Outcome}. Report written to {path}");
        }

        return report.Outcome == "passed" ? 0 : 1;
    }

    private static bool LooksNonProduction(Uri baseUrl)
    {
        var host = baseUrl.Host.ToLowerInvariant();
        if (baseUrl.IsLoopback || host is "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal))
            return true;

        string[] markers = ["dev", "test", "staging", "stage", "drill", "sandbox", "preview", "qa"];
        return host.EndsWith(".local", StringComparison.Ordinal)
            || host.EndsWith(".internal", StringComparison.Ordinal)
            || host.Split('.', '-').Any(part => markers.Contains(part));
    }

    private static void WriteAuthDrillHelp()
    {
        Console.WriteLine("auth-drill options:");
        Console.WriteLine("  --base-url <url>            API origin, such as http://localhost:8090. Required.");
        Console.WriteLine("  --mailpit-url <url>         Mailpit origin catching the API's email, such as http://localhost:8025. Required.");
        Console.WriteLine("  --prometheus-url <url>      Prometheus origin. Needed for the telemetry scenario and its privacy scan.");
        Console.WriteLine("  --environment <name>        deployment_environment_name to check in Prometheus. Default: any.");
        Console.WriteLine("  --scenario <name>           all (default), session-revocation, token-reuse, recovery, or telemetry.");
        Console.WriteLine("  --report <path>             Report path relative to the repo root. Default: .tmp/auth-drill/<run>.json");
        Console.WriteLine("  --confirm-non-production    Run against a host that does not look like a non-production one.");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  dotnet run --project tools/Event.DevTasks -- auth-drill --base-url http://localhost:8090 --mailpit-url http://localhost:8025 --prometheus-url http://localhost:9090");
        Console.WriteLine();
    }

    private sealed class AuthDrillOptions
    {
        public Uri? BaseUrl { get; set; }
        public Uri? MailpitUrl { get; set; }
        public Uri? PrometheusUrl { get; set; }
        public string? Environment { get; set; }
        public string Scenario { get; set; } = "all";
        public string? ReportPath { get; set; }
        public bool ConfirmedNonProduction { get; set; }
    }

    private sealed class AuthDrill
    {
        private static readonly TimeSpan MailWait = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan HistoryWait = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan MetricsWait = TimeSpan.FromSeconds(120);

        private readonly AuthDrillOptions _options;
        private readonly DrillReport _report;
        private readonly Mailbox _mailbox;
        private readonly HttpClient _prometheus;
        private readonly List<string> _canaries = [];
        private readonly string _email;
        private readonly string _username;
        private string _password;

        private DrillDevice? _deviceA;
        private DrillSession? _sessionA;
        private bool _accountCreated;

        public AuthDrill(AuthDrillOptions options, string runId, DrillReport report)
        {
            _options = options;
            _report = report;
            _mailbox = new Mailbox(options.MailpitUrl!);
            _prometheus = new HttpClient { BaseAddress = options.PrometheusUrl, Timeout = TimeSpan.FromSeconds(30) };
            _email = $"auth-drill-{runId}@example.com";
            _username = $"drill{runId}";
            _password = NewPassword();
            _canaries.AddRange([_email, _username, _password]);
        }

        private bool Runs(string scenario) => _options.Scenario == "all" || _options.Scenario == scenario;

        public async Task RunAsync()
        {
            var telemetry = Runs("telemetry") && _options.PrometheusUrl is not null;
            if (Runs("telemetry") && !telemetry)
                _report.Scenario("telemetry").Skip("no --prometheus-url");

            var baseline = telemetry ? await ReadCountersAsync() : null;
            var setup = _report.Scenario("setup");
            try
            {
                await SetUpAccountAsync(setup);
            }
            catch (DrillFailure failure)
            {
                setup.Fail(failure.Message);
                return;
            }

            try
            {
                await RunScenarioAsync("session-revocation", SessionRevocationAsync);
                await RunScenarioAsync("token-reuse", TokenReuseAsync);
                await RunScenarioAsync("recovery", RecoveryAsync);
                if (telemetry)
                    await RunScenarioAsync("telemetry", scenario => AuthorizationDenialAsync(scenario));
            }
            finally
            {
                var cleanup = _report.Scenario("cleanup");
                try
                {
                    await DeleteAccountAsync(cleanup);
                }
                catch (DrillFailure failure)
                {
                    cleanup.Fail(failure.Message + " Delete the drill account by hand; its id is in the step log.");
                }
            }

            // After cleanup, so the throttling it provokes cannot block the steps above.
            if (telemetry)
                await RunScenarioAsync("telemetry", scenario => TelemetryAsync(scenario, baseline!), append: true);
        }

        private async Task RunScenarioAsync(string name, Func<DrillScenario, Task> run, bool append = false)
        {
            if (!Runs(name))
                return;

            var scenario = _report.Scenario(name, append);
            try
            {
                await run(scenario);
            }
            catch (DrillFailure failure)
            {
                scenario.Fail(failure.Message);
            }
            catch (HttpRequestException exception)
            {
                scenario.Fail($"HTTP request failed: {exception.Message}");
            }
        }

        // --------------------------------------------------------------- setup and cleanup

        private async Task SetUpAccountAsync(DrillScenario scenario)
        {
            _deviceA = new DrillDevice(_options.BaseUrl!, "device A");

            await scenario.StepAsync("sign up a throwaway account", async step =>
            {
                var response = await _deviceA.PostAnonymousAsync("/api/auth/signup", new
                {
                    email = _email,
                    username = _username,
                    password = _password,
                    usertype = "Participant",
                    captcha = "auth-drill",
                });
                await step.ExpectAsync(response, HttpStatusCode.OK);
                _accountCreated = true;
            });

            var token = await scenario.StepAsync("receive the verification email", _ =>
                _mailbox.WaitForTokenAsync(_email, "Verify your email", MailWait));
            _canaries.Add(token);

            _sessionA = await scenario.StepAsync("verify the email and sign in on device A", async step =>
            {
                var response = await _deviceA.PostAnonymousAsync("/api/auth/verify", new { token, transport = "api" });
                await step.ExpectAsync(response, HttpStatusCode.OK);
                return Session(await ReadDataAsync(response));
            });
        }

        private async Task DeleteAccountAsync(DrillScenario scenario)
        {
            if (!_accountCreated)
            {
                scenario.Skip("no account was created");
                return;
            }

            var session = _sessionA;
            if (session is null
                || (await _deviceA!.SendAsync(HttpMethod.Get, "/api/auth/me", session.AccessToken)).StatusCode != HttpStatusCode.OK)
            {
                session = await scenario.StepAsync("sign in to delete the account", _ => SignInAsync(_deviceA!, scenario));
            }

            await StepUpAsync(scenario, _deviceA!, session);

            await scenario.StepAsync("delete the drill account", async step =>
            {
                var response = await _deviceA!.SendAsync(HttpMethod.Delete, "/api/profile", session.AccessToken);
                await step.ExpectAsync(response, HttpStatusCode.OK);
            });

            await scenario.StepAsync("its access token stops working", async step =>
            {
                await step.ExpectAsync(await _deviceA!.SendAsync(HttpMethod.Get, "/api/auth/me", session.AccessToken), HttpStatusCode.Unauthorized);
            });
        }

        // --------------------------------------------------------------- scenarios

        private async Task SessionRevocationAsync(DrillScenario scenario)
        {
            var deviceB = new DrillDevice(_options.BaseUrl!, "device B");
            var sessionB = await scenario.StepAsync("sign in on a new device B", _ => SignInAsync(deviceB, scenario));

            await StepUpAsync(scenario, _deviceA!, _sessionA!);

            await scenario.StepAsync("sign out every other session from device A", async step =>
            {
                await step.ExpectAsync(await _deviceA!.SendAsync(HttpMethod.Delete, "/api/auth/sessions/others", _sessionA!.AccessToken), HttpStatusCode.OK);
            });

            await scenario.StepAsync("device B's access token is refused at once", async step =>
            {
                await step.ExpectAsync(await deviceB.SendAsync(HttpMethod.Get, "/api/auth/me", sessionB.AccessToken), HttpStatusCode.Unauthorized);
            });

            await scenario.StepAsync("device B's refresh token is refused", async step =>
            {
                await step.ExpectAsync(await deviceB.RefreshAsync(sessionB), HttpStatusCode.Unauthorized);
            });

            await scenario.StepAsync("device A's session survives", async step =>
            {
                await step.ExpectAsync(await _deviceA!.SendAsync(HttpMethod.Get, "/api/auth/me", _sessionA!.AccessToken), HttpStatusCode.OK);
            });

            await ExpectHistoryAsync(scenario, _sessionA!, "other_sessions_revoked");
        }

        private async Task TokenReuseAsync(DrillScenario scenario)
        {
            var original = _sessionA!;
            var rotated = await scenario.StepAsync("refresh device A's session", async step =>
            {
                var response = await _deviceA!.RefreshAsync(original);
                await step.ExpectAsync(response, HttpStatusCode.OK);
                return Session(await ReadDataAsync(response));
            });

            await scenario.StepAsync("wait past the 10-second reuse grace window", async _ =>
            {
                await Task.Delay(TimeSpan.FromSeconds(12));
            });

            await scenario.StepAsync("replay the rotated refresh token", async step =>
            {
                await step.ExpectAsync(await _deviceA!.RefreshAsync(original), HttpStatusCode.Unauthorized);
            });

            await scenario.StepAsync("the whole session is revoked, including the newest token", async step =>
            {
                await step.ExpectAsync(await _deviceA!.SendAsync(HttpMethod.Get, "/api/auth/me", rotated.AccessToken), HttpStatusCode.Unauthorized);
                await step.ExpectAsync(await _deviceA!.RefreshAsync(rotated), HttpStatusCode.Unauthorized);
            });

            _sessionA = await scenario.StepAsync("sign in again on device A", _ => SignInAsync(_deviceA!, scenario));
            await ExpectHistoryAsync(scenario, _sessionA, "refresh_token_reuse_detected");
        }

        private async Task RecoveryAsync(DrillScenario scenario)
        {
            var before = _sessionA!;

            await scenario.StepAsync("request a password reset", async step =>
            {
                await step.ExpectAsync(await _deviceA!.PostAnonymousAsync("/api/auth/recovery/password", new { username = _username, captcha = "auth-drill" }), HttpStatusCode.OK);
            });

            var token = await scenario.StepAsync("receive the reset email", _ =>
                _mailbox.WaitForTokenAsync(_email, "Reset your password", MailWait));
            _canaries.Add(token);

            var newPassword = NewPassword();
            _canaries.Add(newPassword);
            await scenario.StepAsync("reset the password from the emailed link", async step =>
            {
                var response = await _deviceA!.PostAnonymousAsync($"/api/auth/reset-password?token={Uri.EscapeDataString(token)}", new { password = newPassword });
                await step.ExpectAsync(response, HttpStatusCode.OK);
                _password = newPassword;
            });

            await scenario.StepAsync("sessions from before the reset are refused", async step =>
            {
                await step.ExpectAsync(await _deviceA!.SendAsync(HttpMethod.Get, "/api/auth/me", before.AccessToken), HttpStatusCode.Unauthorized);
                await step.ExpectAsync(await _deviceA!.RefreshAsync(before), HttpStatusCode.Unauthorized);
            });

            _sessionA = await scenario.StepAsync("sign in with the new password", _ => SignInAsync(_deviceA!, scenario));
            await ExpectHistoryAsync(scenario, _sessionA, "password_reset");
        }

        private async Task AuthorizationDenialAsync(DrillScenario scenario)
        {
            // A session that has not stepped up is refused by [RequireMfa], which records authz.denied.
            var fresh = await scenario.StepAsync("sign in without a step-up proof", _ => SignInAsync(_deviceA!, scenario));
            await scenario.StepAsync("a [RequireMfa] route refuses it with MFA_REQUIRED", async step =>
            {
                var response = await _deviceA!.SendAsync(HttpMethod.Get, "/api/auth/mfa/step-up/status", fresh.AccessToken);
                await step.ExpectAsync(response, HttpStatusCode.Forbidden);
                step.ExpectErrorCode(await response.Content.ReadAsStringAsync(), "MFA_REQUIRED");
            });
            _sessionA = fresh;
        }

        private async Task TelemetryAsync(DrillScenario scenario, IReadOnlyDictionary<string, double> baseline)
        {
            var attacker = new DrillDevice(_options.BaseUrl!, "attacker");
            var unknownUser = $"nobody{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}";

            await scenario.StepAsync("send wrong passwords until the source is throttled", async step =>
            {
                for (var attempt = 1; attempt <= 25; attempt++)
                {
                    var response = await attacker.PostAnonymousAsync("/api/auth/login", new
                    {
                        username = unknownUser,
                        password = "definitely-not-the-password",
                        captcha = "auth-drill",
                        transport = "api",
                    });

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        step.Note($"throttled after {attempt} attempts");
                        return;
                    }

                    if (response.StatusCode != HttpStatusCode.Unauthorized)
                        throw new DrillFailure($"expected 401 or 429, got {(int)response.StatusCode}");
                }

                throw new DrillFailure("the source was not throttled after 25 wrong passwords");
            });

            await scenario.StepAsync("the counters reach Prometheus", async step =>
            {
                var deadline = DateTime.UtcNow + MetricsWait;
                Dictionary<string, double> current;
                string[] missing;
                do
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    current = await ReadCountersAsync();
                    missing = current.Where(counter => counter.Value <= baseline.GetValueOrDefault(counter.Key)).Select(counter => counter.Key).ToArray();
                }
                while (missing.Length > 0 && DateTime.UtcNow < deadline);

                if (missing.Length > 0)
                    throw new DrillFailure($"these counters did not move within {MetricsWait.TotalSeconds:0}s: {string.Join(", ", missing)}");

                step.Note(string.Join(", ", current.Select(counter => $"{counter.Key} +{counter.Value - baseline.GetValueOrDefault(counter.Key):0}")));
            });

            await scenario.StepAsync("monitoring holds no prohibited labels or values", async step =>
            {
                var problems = await ScanPrometheusAsync();
                if (problems.Count > 0)
                    throw new DrillFailure(string.Join("; ", problems.Take(10)));
                step.Note("label names within the allowlist; no email, address, token, hash, or drill canary in any value");
            });
        }

        // --------------------------------------------------------------- building blocks

        private async Task<DrillSession> SignInAsync(DrillDevice device, DrillScenario? scenario)
        {
            var response = await device.PostAnonymousAsync("/api/auth/login", new
            {
                username = _username,
                password = _password,
                captcha = "auth-drill",
                transport = "api",
            });
            var body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var data = JsonNode.Parse(body)?["data"];
                if (data?["type"]?.GetValue<string>() != "authenticated")
                    throw new DrillFailure($"sign-in answered '{data?["type"]}', which the drill account should never need");
                return Session(data["auth"]!);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden && ErrorCode(body) == "DEVICE_VERIFICATION_REQUIRED")
            {
                scenario?.Current?.Note("new device: verified by the emailed link");
                var token = await _mailbox.WaitForTokenAsync(_email, "Confirm new device sign-in", MailWait);
                _canaries.Add(token);
                var verified = await device.PostAnonymousAsync("/api/auth/device/verify", new { token, transport = "api" });
                if (verified.StatusCode != HttpStatusCode.OK)
                    throw new DrillFailure($"device verification answered {(int)verified.StatusCode}");
                return Session(await ReadDataAsync(verified));
            }

            throw new DrillFailure(response.StatusCode == HttpStatusCode.TooManyRequests
                ? $"sign-in answered 429 ({DrillLimitsHint})"
                : $"sign-in answered {(int)response.StatusCode} {ErrorCode(body)}");
        }

        private async Task StepUpAsync(DrillScenario scenario, DrillDevice device, DrillSession session)
        {
            await scenario.StepAsync("start an email step-up", async step =>
            {
                await step.ExpectAsync(await device.SendAsync(HttpMethod.Post, "/api/auth/mfa/step-up/start", session.AccessToken, new { method = "email" }), HttpStatusCode.OK);
            });

            var code = await scenario.StepAsync("receive the step-up code", _ =>
                _mailbox.WaitForCodeAsync(_email, "Your verification code", MailWait));
            _canaries.Add(code);

            await scenario.StepAsync("verify the step-up code", async step =>
            {
                await step.ExpectAsync(await device.SendAsync(HttpMethod.Post, "/api/auth/mfa/step-up/verify", session.AccessToken, new { method = "email", code }), HttpStatusCode.OK);
            });
        }

        private async Task ExpectHistoryAsync(DrillScenario scenario, DrillSession session, string kind)
        {
            await scenario.StepAsync($"the account history records {kind}", async step =>
            {
                var deadline = DateTime.UtcNow + HistoryWait;
                while (true)
                {
                    var response = await _deviceA!.SendAsync(HttpMethod.Get, "/api/auth/security-events?page=1&pageSize=50", session.AccessToken);
                    await step.ExpectAsync(response, HttpStatusCode.OK);
                    var body = await response.Content.ReadAsStringAsync();
                    if (Kinds(body).Contains(kind))
                        return;
                    if (DateTime.UtcNow > deadline)
                        throw new DrillFailure($"no '{kind}' entry within {HistoryWait.TotalSeconds:0}s; the history pipeline (outbox, Kafka Connect, worker) may be down");
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }
            });
        }

        private async Task<Dictionary<string, double>> ReadCountersAsync()
        {
            var env = string.IsNullOrWhiteSpace(_options.Environment) ? string.Empty : $", deployment_environment_name=\"{_options.Environment}\"";
            var queries = new Dictionary<string, string>
            {
                ["sign_in_failed_invalid_credentials"] = $"sum(auth_security_events_total{{event=\"auth.sign_in.failed\", reason=\"invalid_credentials\"{env}}})",
                ["throttled"] = $"sum(auth_security_events_total{{event=\"auth.throttled\"{env}}})",
                ["authz_denied_mfa_required"] = $"sum(auth_security_events_total{{event=\"authz.denied\", reason=\"mfa_required\"{env}}})",
                ["sign_in_succeeded"] = $"sum(auth_security_events_total{{event=\"auth.sign_in.succeeded\"{env}}})",
                ["auth_operations"] = $"sum(auth_operation_duration_milliseconds_count{{operation=\"sign_in\"{env}}})",
            };

            var values = new Dictionary<string, double>();
            foreach (var (name, query) in queries)
            {
                var result = await QueryPrometheusAsync(query);
                values[name] = result.FirstOrDefault()?["value"]?[1]?.GetValue<string>() is { } text
                    ? double.Parse(text, CultureInfo.InvariantCulture)
                    : 0;
            }

            return values;
        }

        private async Task<JsonArray> QueryPrometheusAsync(string query)
        {
            var response = await _prometheus.GetAsync($"/api/v1/query?query={Uri.EscapeDataString(query)}");
            response.EnsureSuccessStatusCode();
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())?["data"]?["result"]?.AsArray() ?? [];
        }

        private async Task<List<string>> ScanPrometheusAsync()
        {
            string[] allowedLabels =
            [
                "__name__", "job", "instance", "deployment_environment_name", "event", "outcome", "reason", "method",
                "flow", "dimension", "factor", "operation", "sink", "message_type", "le", "otel_scope_name",
                "otel_scope_version", "otel_scope_schema_url",
            ];
            Regex[] prohibitedValues =
            [
                IdentifierPatterns.Email(), IdentifierPatterns.Ipv4(), IdentifierPatterns.Ipv6(),
                IdentifierPatterns.Jwt(), IdentifierPatterns.LongHex(),
            ];

            var problems = new List<string>();
            var names = JsonNode.Parse(await _prometheus.GetStringAsync("/api/v1/label/__name__/values"))?["data"]?.AsArray()
                .Select(name => name!.GetValue<string>())
                .Where(name => name.StartsWith("auth_", StringComparison.Ordinal)
                    || name.StartsWith("security_history_", StringComparison.Ordinal)
                    || name.StartsWith("notifications_", StringComparison.Ordinal))
                .ToArray() ?? [];

            if (names.Length == 0)
                problems.Add("no auth, security history, or notification series found");

            foreach (var name in names)
            {
                var series = JsonNode.Parse(await _prometheus.GetStringAsync($"/api/v1/series?match[]={Uri.EscapeDataString(name)}"))?["data"]?.AsArray() ?? [];
                foreach (var labels in series.Select(item => item!.AsObject()))
                {
                    foreach (var (label, valueNode) in labels)
                    {
                        var value = valueNode?.GetValue<string>() ?? string.Empty;
                        if (!allowedLabels.Contains(label))
                            problems.Add($"{name} has label '{label}'");
                        if (label is "instance" or "__name__")
                            continue;
                        if (prohibitedValues.Any(pattern => pattern.IsMatch(value)) || _canaries.Any(canary => value.Contains(canary, StringComparison.Ordinal)))
                            problems.Add($"{name}{{{label}}} has a prohibited value");
                    }
                }
            }

            return problems.Distinct().ToList();
        }

        private static DrillSession Session(JsonNode data) => new(
            data["accessToken"]?.GetValue<string>() ?? throw new DrillFailure("the response carried no access token"),
            data["refreshToken"]?.GetValue<string>() ?? throw new DrillFailure("the response carried no refresh token; was transport=api honoured?"),
            data["sessionBindingToken"]?.GetValue<string>());

        private static async Task<JsonNode> ReadDataAsync(HttpResponseMessage response) =>
            JsonNode.Parse(await response.Content.ReadAsStringAsync())?["data"] ?? throw new DrillFailure("the response had no data");

        private static string? ErrorCode(string body)
        {
            try
            {
                return JsonNode.Parse(body)?["error"]?["code"]?.GetValue<string>();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static HashSet<string> Kinds(string body)
        {
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            var data = JsonNode.Parse(body)?["data"];
            var items = data?["items"]?.AsArray() ?? data?["data"]?.AsArray() ?? [];
            foreach (var item in items)
            {
                if (item?["kind"]?.GetValue<string>() is { } kind)
                    kinds.Add(kind);
            }

            return kinds;
        }

        // Random and unrelated to the username, email, and site name, so the password policy accepts it.
        private static string NewPassword() =>
            $"quartz {Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant()} lantern {Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant()}";
    }

    private sealed record DrillSession(string AccessToken, string RefreshToken, string? SessionBindingToken);

    /// <summary>One browser-like client: its own cookies (CSRF and trusted-device), so each is a device.</summary>
    private sealed class DrillDevice
    {
        private readonly HttpClient _client;

        public DrillDevice(Uri baseUrl, string name)
        {
            Name = name;
            _client = new HttpClient(new SocketsHttpHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                AllowAutoRedirect = false,
            })
            {
                BaseAddress = baseUrl,
                Timeout = TimeSpan.FromSeconds(30),
            };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/126.0 Safari/537.36 EventXperienceAuthDrill/1");
        }

        public string Name { get; }

        public async Task<HttpResponseMessage> PostAnonymousAsync(string path, object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent(body) };
            request.Headers.Add("X-CSRF-TOKEN", await CsrfTokenAsync());
            return await DrillScenario.TrackAsync(_client.SendAsync(request));
        }

        // Like the browser client, every unsafe request carries the CSRF header: this device holds
        // the antiforgery cookie, so the API checks it even on bearer-authenticated calls.
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken, object? body = null)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (method != HttpMethod.Get)
                request.Headers.Add("X-CSRF-TOKEN", await CsrfTokenAsync());
            if (body is not null)
                request.Content = JsonContent(body);
            return await DrillScenario.TrackAsync(_client.SendAsync(request));
        }

        private async Task<string> CsrfTokenAsync()
        {
            var csrf = await _client.GetAsync("/api/auth/csrf");
            csrf.EnsureSuccessStatusCode();
            return JsonNode.Parse(await csrf.Content.ReadAsStringAsync())?["data"]?["token"]?.GetValue<string>()
                ?? throw new DrillFailure("no CSRF token");
        }

        public Task<HttpResponseMessage> RefreshAsync(DrillSession session)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/api/refresh")
            {
                Content = JsonContent(new { refreshToken = session.RefreshToken, sessionBindingToken = session.SessionBindingToken }),
            };
            return DrillScenario.TrackAsync(_client.SendAsync(request));
        }

        private static StringContent JsonContent(object body) =>
            new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    }

    /// <summary>Reads the drill account's email from Mailpit's API.</summary>
    private sealed partial class Mailbox(Uri mailpitUrl)
    {
        private readonly HttpClient _client = new() { BaseAddress = mailpitUrl, Timeout = TimeSpan.FromSeconds(30) };
        private readonly HashSet<string> _seen = [];

        public async Task<string> WaitForTokenAsync(string to, string subject, TimeSpan wait)
        {
            var text = await WaitForMessageAsync(to, subject, wait);
            var match = LinkToken().Match(text);
            return match.Success
                ? Uri.UnescapeDataString(match.Groups["token"].Value)
                : throw new DrillFailure($"the '{subject}' email has no token link");
        }

        public async Task<string> WaitForCodeAsync(string to, string subject, TimeSpan wait)
        {
            var text = await WaitForMessageAsync(to, subject, wait);
            var match = SixDigitCode().Match(text);
            return match.Success ? match.Value : throw new DrillFailure($"the '{subject}' email has no code");
        }

        private async Task<string> WaitForMessageAsync(string to, string subject, TimeSpan wait)
        {
            var deadline = DateTime.UtcNow + wait;
            var query = Uri.EscapeDataString($"to:\"{to}\" subject:\"{subject}\"");
            while (DateTime.UtcNow < deadline)
            {
                var search = JsonNode.Parse(await _client.GetStringAsync($"/api/v1/search?query={query}&limit=20"));
                var id = search?["messages"]?.AsArray()
                    .Select(message => message?["ID"]?.GetValue<string>())
                    .FirstOrDefault(candidate => candidate is not null && !_seen.Contains(candidate));
                if (id is not null)
                {
                    _seen.Add(id);
                    var message = JsonNode.Parse(await _client.GetStringAsync($"/api/v1/message/{id}"));
                    return (message?["Text"]?.GetValue<string>() ?? string.Empty) + "\n" + (message?["HTML"]?.GetValue<string>() ?? string.Empty);
                }

                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            throw new DrillFailure($"no '{subject}' email within {wait.TotalSeconds:0}s; is email-worker pointed at Mailpit?");
        }

        [GeneratedRegex(@"[?&]token=(?<token>[^""&\s<>]+)")]
        private static partial Regex LinkToken();

        [GeneratedRegex(@"(?<!\d)\d{6}(?!\d)")]
        private static partial Regex SixDigitCode();
    }

    private static partial class IdentifierPatterns
    {
        [GeneratedRegex(@"[^\s@]+@[^\s@]+\.[^\s@]+")]
        public static partial Regex Email();

        [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
        public static partial Regex Ipv4();

        [GeneratedRegex(@"\b(?:[0-9a-fA-F]{1,4}:){4,7}[0-9a-fA-F]{1,4}\b")]
        public static partial Regex Ipv6();

        [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")]
        public static partial Regex Jwt();

        [GeneratedRegex(@"\b[0-9a-fA-F]{32,}\b")]
        public static partial Regex LongHex();
    }

    private sealed class DrillFailure(string message) : Exception(message);

    /// <summary>The auditable drill record. It holds outcomes, timings, and request ids, never secrets.</summary>
    private sealed class DrillReport(string runId, string? environment, string target, DateTime startedAtUtc)
    {
        private readonly List<DrillScenario> _scenarios = [];

        public DateTime FinishedAtUtc { get; set; }

        public string Outcome => _scenarios.Any(scenario => scenario.Outcome == "failed") ? "failed" : "passed";

        public DrillScenario Scenario(string name, bool append = false)
        {
            var existing = append ? _scenarios.LastOrDefault(scenario => scenario.Name == name) : null;
            if (existing is not null)
            {
                Console.WriteLine();
                Console.WriteLine($"== {name} (continued)");
                return existing;
            }

            var scenario = new DrillScenario(name);
            _scenarios.Add(scenario);
            Console.WriteLine();
            Console.WriteLine($"== {name}");
            return scenario;
        }

        public string ToJson() => JsonSerializer.Serialize(new
        {
            runId,
            tool = "Event.DevTasks auth-drill",
            environment,
            target,
            startedAtUtc,
            finishedAtUtc = FinishedAtUtc,
            outcome = Outcome,
            scenarios = _scenarios.Select(scenario => new
            {
                scenario.Name,
                scenario.Outcome,
                scenario.Detail,
                steps = scenario.Steps.Select(step => new { step.Name, step.Outcome, step.DurationMs, step.RequestIds, step.Detail }),
            }),
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private sealed class DrillScenario(string name)
    {
        private static readonly AsyncLocal<DrillStep?> ActiveStep = new();

        public string Name { get; } = name;
        public List<DrillStep> Steps { get; } = [];
        public string? Detail { get; private set; }
        public string Outcome { get; private set; } = "passed";
        public DrillStep? Current => ActiveStep.Value;

        public static async Task<HttpResponseMessage> TrackAsync(Task<HttpResponseMessage> sending)
        {
            var response = await sending;
            if (response.Headers.TryGetValues("X-Request-Id", out var ids))
                ActiveStep.Value?.RequestIds.AddRange(ids);
            return response;
        }

        public async Task StepAsync(string step, Func<DrillStep, Task> run) =>
            await StepAsync<object?>(step, async current =>
            {
                await run(current);
                return null;
            });

        public async Task<T> StepAsync<T>(string step, Func<DrillStep, Task<T>> run)
        {
            var current = new DrillStep(step);
            Steps.Add(current);
            ActiveStep.Value = current;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await run(current);
                current.Outcome = "passed";
                return result;
            }
            catch (Exception exception) when (exception is DrillFailure or HttpRequestException or TaskCanceledException)
            {
                current.Outcome = "failed";
                current.Detail = exception.Message;
                throw exception as DrillFailure ?? new DrillFailure(exception.Message);
            }
            finally
            {
                current.DurationMs = stopwatch.ElapsedMilliseconds;
                ActiveStep.Value = null;
                Console.WriteLine($"  [{current.Outcome}] {step} ({current.DurationMs} ms){(current.Detail is null ? string.Empty : $": {current.Detail}")}");
            }
        }

        public void Fail(string detail)
        {
            Outcome = "failed";
            Detail = detail;
        }

        public void Skip(string detail)
        {
            Outcome = "skipped";
            Detail = detail;
            Console.WriteLine($"  [skipped] {detail}");
        }
    }

    private sealed class DrillStep(string name)
    {
        public string Name { get; } = name;
        public string Outcome { get; set; } = "running";
        public long DurationMs { get; set; }
        public List<string> RequestIds { get; } = [];
        public string? Detail { get; set; }

        public async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            if (response.StatusCode == expected)
                return;

            // The API's error code and message are generic; they never echo credentials or tokens.
            var detail = string.Empty;
            try
            {
                var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                var fields = body?["error"]?["details"]?.AsObject().Select(field => field.Key) ?? [];
                detail = $" {body?["error"]?["code"]} \"{body?["message"]}\"{(fields.Any() ? $" fields: {string.Join(", ", fields)}" : string.Empty)}";
            }
            catch (JsonException)
            {
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                detail += $" ({DrillLimitsHint})";

            throw new DrillFailure($"expected {(int)expected}, got {(int)response.StatusCode}{detail}");
        }

        public void ExpectErrorCode(string body, string code)
        {
            var actual = JsonNode.Parse(body)?["error"]?["code"]?.GetValue<string>();
            if (actual != code)
                throw new DrillFailure($"expected error code {code}, got {actual ?? "none"}");
        }

        public void Note(string note) => Detail = Detail is null ? note : $"{Detail}; {note}";
    }
}
