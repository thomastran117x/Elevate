using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

return await DevTasksCli.RunAsync(args);

internal static partial class DevTasksCli
{
    private const string BackendSolutionPath = "backend.sln";
    private const string BackendProjectPath = "backend/backend.csproj";
    private const string BackendUnitTestProjectPath = "backend.tests.Unit/backend.tests.Unit.csproj";
    private const string BackendIntegrationTestProjectPath =
        "backend.tests.Integration/backend.tests.Integration.csproj";
    private const string BackendCoverageRunSettingsPath = "backend.coverage.runsettings";
    private const string BackendUnitCoverageResultsPath = ".tmp/backend-unit-coverage";
    private const string BackendMainRootPath = "backend/src/main";
    private const string BackendRoutesPath = "backend/src/main/application/bootstrap/Routes.cs";
    private const string BackendIntegrationRootPath = "backend.tests.Integration";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                WriteHelp();
                return 0;
            }

            var command = args[0].ToLowerInvariant();
            return command switch
            {
                "backend-format" => await RunBackendFormatAsync(args[1..]),
                "backend-unit-coverage" => await RunBackendUnitCoverageAsync(args[1..]),
                "backend-integration-tests" => await RunBackendIntegrationTestsAsync(args[1..]),
                "backend-integration-endpoint-coverage" =>
                    await RunBackendIntegrationEndpointCoverageAsync(args[1..]),
                "export-openapi" => await ExportOpenApiAsync(args[1..]),
                _ => Fail($"Unknown command '{args[0]}'.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> RunBackendFormatAsync(string[] args)
    {
        var verifyNoChanges = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--verify-no-changes":
                    verifyNoChanges = true;
                    break;
                case var option when IsHelp(option):
                    WriteBackendFormatHelp();
                    return 0;
                default:
                    return Fail($"Unknown backend-format option '{args[index]}'.");
            }
        }

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        await RunProcessOrThrowAsync("dotnet", ["restore", BackendSolutionPath], repoRoot);

        var formatArguments = new List<string> { "format", BackendSolutionPath, "--no-restore" };
        if (verifyNoChanges)
        {
            formatArguments.Add("--verify-no-changes");
        }

        await RunProcessOrThrowAsync("dotnet", formatArguments, repoRoot);
        return 0;
    }

    private static async Task<int> RunBackendUnitCoverageAsync(string[] args)
    {
        var threshold = 90m;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--threshold":
                case "-t":
                    threshold = decimal.Parse(
                        ReadRequiredValue(args, ref index, "threshold"),
                        CultureInfo.InvariantCulture
                    );
                    break;
                case var option when IsHelp(option):
                    WriteBackendUnitCoverageHelp();
                    return 0;
                default:
                    return Fail($"Unknown backend-unit-coverage option '{args[index]}'.");
            }
        }

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var projectPath = Path.Combine(repoRoot, BackendUnitTestProjectPath);
        var runSettingsPath = Path.Combine(repoRoot, BackendCoverageRunSettingsPath);
        var resultsRoot = Path.Combine(repoRoot, BackendUnitCoverageResultsPath);

        if (Directory.Exists(resultsRoot))
        {
            Directory.Delete(resultsRoot, recursive: true);
        }

        Directory.CreateDirectory(resultsRoot);

        await RunProcessOrThrowAsync(
            "dotnet",
            [
                "test",
                projectPath,
                "--configuration",
                "Release",
                "--settings",
                runSettingsPath,
                "--collect:XPlat Code Coverage",
                "--results-directory",
                resultsRoot
            ],
            repoRoot
        );

        var reportPath = Directory
            .GetFiles(resultsRoot, "coverage.cobertura.xml", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (reportPath is null)
        {
            throw new InvalidOperationException(
                $"Coverage report was not generated under '{resultsRoot}'."
            );
        }

        var report = XDocument.Load(reportPath);
        var coverage = report.Root ?? throw new InvalidOperationException("Coverage XML was empty.");

        var linesCovered = GetDecimalAttribute(coverage, "lines-covered");
        var linesValid = GetDecimalAttribute(coverage, "lines-valid");
        var branchesCovered = GetDecimalAttribute(coverage, "branches-covered");
        var branchesValid = GetDecimalAttribute(coverage, "branches-valid");

        if (linesValid <= 0)
        {
            throw new InvalidOperationException("Coverage report did not contain any valid lines.");
        }

        var lineCoverage = linesCovered * 100m / linesValid;
        var branchCoverage = branchesValid > 0 ? branchesCovered * 100m / branchesValid : 0m;

        Console.WriteLine(
            $"Backend unit coverage: {lineCoverage:F2}% ({linesCovered}/{linesValid})"
        );
        Console.WriteLine(
            $"Backend unit branch coverage: {branchCoverage:F2}% ({branchesCovered}/{branchesValid})"
        );

        if (lineCoverage < threshold)
        {
            throw new InvalidOperationException(
                $"Coverage threshold not met. Required: {threshold:F2}% Actual: {lineCoverage:F2}%"
            );
        }

        return 0;
    }

    private static async Task<int> RunBackendIntegrationTestsAsync(string[] args)
    {
        string? filter = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--filter":
                    filter = ReadRequiredValue(args, ref index, "filter");
                    break;
                case var option when IsHelp(option):
                    WriteBackendIntegrationTestsHelp();
                    return 0;
                default:
                    return Fail($"Unknown backend-integration-tests option '{args[index]}'.");
            }
        }

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var projectPath = Path.Combine(repoRoot, BackendIntegrationTestProjectPath);

        var testArguments = new List<string>
        {
            "test",
            projectPath,
            "--configuration",
            "Release"
        };

        if (!string.IsNullOrWhiteSpace(filter))
        {
            testArguments.Add("--filter");
            testArguments.Add(filter);
        }

        await RunProcessOrThrowAsync("dotnet", testArguments, repoRoot);
        return 0;
    }

    private static Task<int> RunBackendIntegrationEndpointCoverageAsync(string[] args)
    {
        var failOnMissing = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--fail-on-missing":
                    failOnMissing = true;
                    break;
                case var option when IsHelp(option):
                    WriteBackendIntegrationEndpointCoverageHelp();
                    return Task.FromResult(0);
                default:
                    return Task.FromResult(
                        Fail(
                            $"Unknown backend-integration-endpoint-coverage option '{args[index]}'."
                        )
                    );
            }
        }

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var backendRoot = Path.Combine(repoRoot, BackendMainRootPath);
        var integrationRoot = Path.Combine(repoRoot, BackendIntegrationRootPath);
        var routesFile = Path.Combine(repoRoot, BackendRoutesPath);

        var routeConstants = GetRouteConstants(routesFile);
        var controllerEndpoints = GetControllerEndpoints(backendRoot, routeConstants);
        var integrationRequests = GetIntegrationRequests(integrationRoot);

        var requestKeyCounts = integrationRequests
            .GroupBy(request => $"{request.Verb} {request.Route}")
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var results = controllerEndpoints
            .Select(endpoint =>
            {
                var key = $"{endpoint.Verb} {endpoint.Route}";
                var matchCount = requestKeyCounts.GetValueOrDefault(key, 0);

                return new EndpointCoverageResult(
                    endpoint.Controller,
                    endpoint.Action,
                    endpoint.Verb,
                    endpoint.Route,
                    matchCount > 0,
                    matchCount,
                    endpoint.Source,
                    endpoint.Line
                );
            })
            .ToArray();

        var total = results.Length;
        var covered = results.Count(result => result.Covered);
        var missing = results.Where(result => !result.Covered).ToArray();
        var coveragePercent = total > 0 ? Math.Round(covered * 100.0 / total, 2) : 0.0;

        Console.WriteLine(
            $"Integration endpoint coverage: {coveragePercent:F2}% ({covered}/{total} controller actions)"
        );
        Console.WriteLine();
        Console.WriteLine("By controller:");

        foreach (
            var group in results
                .GroupBy(result => result.Controller)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
        )
        {
            var controllerCovered = group.Count(result => result.Covered);
            var controllerTotal = group.Count();
            var controllerPercent = Math.Round(controllerCovered * 100.0 / controllerTotal, 2);

            Console.WriteLine(
                $"  {group.Key,-28} {controllerCovered,3}/{controllerTotal,-3} {controllerPercent,6:F2}%"
            );
        }

        if (missing.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Endpoints without a matching integration request:");

            foreach (
                var endpoint in missing
                    .OrderBy(result => result.Controller, StringComparer.Ordinal)
                    .ThenBy(result => result.Route, StringComparer.Ordinal)
                    .ThenBy(result => result.Verb, StringComparer.Ordinal)
            )
            {
                Console.WriteLine(
                    $"  {endpoint.Controller,-28} {endpoint.Verb,-6} {endpoint.Route} ({endpoint.Action})"
                );
            }
        }

        if (failOnMissing && missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Integration endpoint audit failed with {missing.Length} uncovered controller actions."
            );
        }

        return Task.FromResult(0);
    }

    private static async Task<int> ExportOpenApiAsync(string[] args)
    {
        var outputPath = "backend/openapi.yaml";
        var port = 8090;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--output":
                case "-o":
                    outputPath = ReadRequiredValue(args, ref index, "output path");
                    break;
                case "--port":
                case "-p":
                    port = int.Parse(ReadRequiredValue(args, ref index, "port"));
                    break;
                case var option when IsHelp(option):
                    WriteExportOpenApiHelp();
                    return 0;
                default:
                    return Fail($"Unknown export-openapi option '{args[index]}'.");
            }
        }

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var backendDirectory = Path.Combine(repoRoot, "backend");
        var backendProjectPath = Path.Combine(repoRoot, BackendProjectPath);
        var resolvedOutputPath = Path.GetFullPath(Path.Combine(repoRoot, outputPath));
        var outputDirectory = Path.GetDirectoryName(resolvedOutputPath)
            ?? throw new InvalidOperationException(
                "Unable to determine the OpenAPI output directory."
            );
        var extension = Path.GetExtension(resolvedOutputPath).ToLowerInvariant();

        var (jsonOutputPath, yamlOutputPath) = extension switch
        {
            ".json" => (resolvedOutputPath, Path.ChangeExtension(resolvedOutputPath, ".yaml")),
            ".yaml" or ".yml" => (Path.ChangeExtension(resolvedOutputPath, ".json"), resolvedOutputPath),
            _ => throw new InvalidOperationException(
                $"Unsupported OpenAPI output extension '{extension}'. Use .json, .yaml, or .yml."
            )
        };

        Directory.CreateDirectory(outputDirectory);

        await RunProcessOrThrowAsync("dotnet", ["build", backendProjectPath], repoRoot);

        var backendDll = FindBuiltBackendAssembly(backendDirectory);
        var outputBuffer = new ConcurrentQueue<string>();
        using var backendProcess = StartProcess(
            "dotnet",
            [backendDll],
            backendDirectory,
            new Dictionary<string, string?>
            {
                ["PORT"] = port.ToString(CultureInfo.InvariantCulture),
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["OPENAPI_EXPORT"] = "true",
                ["OPENAPI_INCLUDE_PREFIX"] = null,
                ["OPENAPI_SERVER_URL"] = $"http://127.0.0.1:{port}"
            },
            captureOutput: true,
            outputBuffer: outputBuffer
        );

        try
        {
            await DownloadFileWithRetryAsync(
                $"http://127.0.0.1:{port}/openapi.json",
                jsonOutputPath,
                backendProcess,
                outputBuffer
            );
            await DownloadFileWithRetryAsync(
                $"http://127.0.0.1:{port}/openapi.yaml",
                yamlOutputPath,
                backendProcess,
                outputBuffer
            );
            Console.WriteLine($"OpenAPI JSON exported to {jsonOutputPath}");
            Console.WriteLine($"OpenAPI YAML exported to {yamlOutputPath}");
            return 0;
        }
        finally
        {
            await StopProcessAsync(backendProcess);
        }
    }

    private static decimal GetDecimalAttribute(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Coverage report is missing the '{name}' attribute.");
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, string> GetRouteConstants(string path)
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(path))
        {
            var match = RouteConstantRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            constants[match.Groups["name"].Value] = match.Groups["value"].Value;
        }

        return constants;
    }

    private static EndpointDefinition[] GetControllerEndpoints(
        string rootPath,
        IReadOnlyDictionary<string, string> routeConstants
    )
    {
        var endpoints = new List<EndpointDefinition>();
        var files = Directory
            .GetFiles(rootPath, "*Controller.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            string? currentController = null;
            var currentControllerRoute = string.Empty;
            string? pendingClassRoute = null;
            var pendingHttpRoutes = new List<PendingHttpRoute>();

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];

                var routeMatch = RouteAttributeRegex().Match(line);
                if (routeMatch.Success)
                {
                    pendingClassRoute = ResolveAttributeRoute(
                        routeMatch.Groups["route"].Value,
                        routeConstants
                    );
                    continue;
                }

                var httpMatch = HttpAttributeRegex().Match(line);
                if (httpMatch.Success)
                {
                    var verb = httpMatch.Groups["attr"].Value[4..].ToUpperInvariant();
                    var actionRoute = ResolveAttributeRoute(
                        httpMatch.Groups["route"].Value,
                        routeConstants
                    );

                    pendingHttpRoutes.Add(new PendingHttpRoute(verb, actionRoute, index + 1));
                    continue;
                }

                var controllerMatch = ControllerClassRegex().Match(line);
                if (controllerMatch.Success)
                {
                    currentController = controllerMatch.Groups["name"].Value;
                    currentControllerRoute = pendingClassRoute ?? string.Empty;
                    pendingClassRoute = null;
                    pendingHttpRoutes.Clear();
                    continue;
                }

                if (pendingHttpRoutes.Count == 0)
                {
                    continue;
                }

                var methodMatch = ControllerMethodRegex().Match(line);
                if (!methodMatch.Success || currentController is null)
                {
                    continue;
                }

                foreach (var httpRoute in pendingHttpRoutes)
                {
                    endpoints.Add(
                        new EndpointDefinition(
                            currentController,
                            methodMatch.Groups["method"].Value,
                            httpRoute.Verb,
                            JoinApiRoute(currentControllerRoute, httpRoute.ActionRoute),
                            file,
                            httpRoute.AttributeLine
                        )
                    );
                }

                pendingHttpRoutes.Clear();
            }
        }

        return endpoints.ToArray();
    }

    private static IntegrationRequest[] GetIntegrationRequests(string rootPath)
    {
        var requests = new List<IntegrationRequest>();
        var files = Directory
            .GetFiles(rootPath, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var patterns = new[]
        {
            new Regex(
                "(?<call>(?<verb>Get|Delete)Async|(?<verb>Post|Put|Patch)(?:AsJson)?Async)\\(\\s*\\$?\"(?<path>/api/[^\"\\r\\n]*)\"",
                RegexOptions.Compiled
            ),
            new Regex(
                "new\\s+HttpRequestMessage\\s*\\(\\s*HttpMethod\\.(?<verb>Get|Post|Put|Delete|Patch)\\s*,\\s*\\$?\"(?<path>/api/[^\"\\r\\n]*)\"",
                RegexOptions.Compiled
            ),
            new Regex(
                "CreateAuthorizedRequest\\s*\\(\\s*HttpMethod\\.(?<verb>Get|Post|Put|Delete|Patch)\\s*,\\s*\\$?\"(?<path>/api/[^\"\\r\\n]*)\"",
                RegexOptions.Compiled
            ),
            new Regex(
                "PostJsonWithCsrfAsync\\(\\s*\\$?\"(?<path>/api/[^\"\\r\\n]*)\"",
                RegexOptions.Compiled
            ),
            new Regex(
                "CreateCsrfRequestAsync\\(\\s*\\w+\\s*,\\s*\\$?\"(?<path>/api/[^\"\\r\\n]*)\"",
                RegexOptions.Compiled
            )
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);

            foreach (var pattern in patterns)
            {
                foreach (Match match in pattern.Matches(content))
                {
                    var verb = match.Groups["verb"].Success && !string.IsNullOrWhiteSpace(match.Groups["verb"].Value)
                        ? match.Groups["verb"].Value.ToUpperInvariant()
                        : "POST";

                    requests.Add(
                        new IntegrationRequest(
                            verb,
                            NormalizeTestPath(match.Groups["path"].Value),
                            file
                        )
                    );
                }
            }
        }

        return requests.ToArray();
    }

    private static string ResolveAttributeRoute(
        string? rawValue,
        IReadOnlyDictionary<string, string> routeConstants
    )
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var value = rawValue.Trim();
        var literalMatch = QuotedLiteralRegex().Match(value);
        if (literalMatch.Success)
        {
            return literalMatch.Groups["literal"].Value;
        }

        var routeConstantMatch = RoutePathReferenceRegex().Match(value);
        if (
            routeConstantMatch.Success
            && routeConstants.TryGetValue(routeConstantMatch.Groups["name"].Value, out var constant)
        )
        {
            return constant;
        }

        return value;
    }

    private static string NormalizeRouteSegment(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return string.Empty;
        }

        var normalized = route.Trim().Replace('\\', '/');
        normalized = RouteParameterRegex().Replace(normalized, "{}");
        return normalized.Trim('/').ToLowerInvariant();
    }

    private static string NormalizeTestPath(string path)
    {
        var normalized = path.Trim().Split('?', 2)[0];
        normalized = SimpleRouteParameterRegex().Replace(normalized, "{}");
        normalized = NumericRouteSegmentRegex().Replace(normalized, "/{}");
        normalized = GuidRouteSegmentRegex().Replace(normalized, "/{}");
        normalized = normalized.TrimEnd('/');

        return string.IsNullOrWhiteSpace(normalized)
            ? "/"
            : normalized.ToLowerInvariant();
    }

    private static string JoinApiRoute(string controllerRoute, string actionRoute)
    {
        var parts = new[]
            {
                NormalizeRouteSegment(controllerRoute),
                NormalizeRouteSegment(actionRoute)
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length == 0 ? "/api" : "/api/" + string.Join('/', parts);
    }

    private static string ReadRequiredValue(string[] args, ref int index, string label)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"Missing {label} after '{args[index]}'.");
        }

        index++;
        return args[index];
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, BackendProjectPath.Replace('/', Path.DirectorySeparatorChar))))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from '{startDirectory}'."
        );
    }

    private static string FindBuiltBackendAssembly(string backendDirectory)
    {
        var binDirectory = Path.Combine(backendDirectory, "bin");
        if (!Directory.Exists(binDirectory))
        {
            throw new InvalidOperationException(
                $"Expected the backend build output directory at '{binDirectory}'."
            );
        }

        var candidates = Directory
            .GetFiles(binDirectory, "backend.dll", SearchOption.AllDirectories)
            .Where(
                path =>
                    !path.Contains(
                        $"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            .Where(
                path =>
                    !path.Contains(
                        $"{Path.DirectorySeparatorChar}refint{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"Could not locate a built backend assembly under '{binDirectory}'."
            );
        }

        return candidates[0].FullName;
    }

    private static Process StartProcess(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        bool captureOutput = false,
        ConcurrentQueue<string>? outputBuffer = null
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environmentVariables is not null)
        {
            foreach (var entry in environmentVariables)
            {
                if (entry.Value is null)
                {
                    startInfo.Environment.Remove(entry.Key);
                }
                else
                {
                    startInfo.Environment[entry.Key] = entry.Value;
                }
            }
        }

        var process = new Process { StartInfo = startInfo };

        if (captureOutput)
        {
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                {
                    outputBuffer?.Enqueue(eventArgs.Data);
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                {
                    outputBuffer?.Enqueue(eventArgs.Data);
                }
            };
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{fileName}'.");
        }

        if (captureOutput)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        return process;
    }

    private static async Task RunProcessOrThrowAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory
    )
    {
        using var process = StartProcess(fileName, arguments, workingDirectory);
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command '{fileName} {string.Join(' ', arguments)}' exited with code {process.ExitCode}."
            );
        }
    }

    private static async Task DownloadFileWithRetryAsync(
        string url,
        string outputPath,
        Process backendProcess,
        ConcurrentQueue<string> outputBuffer
    )
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            try
            {
                await using var responseStream = await httpClient.GetStreamAsync(url);
                await using var fileStream = File.Create(outputPath);
                await responseStream.CopyToAsync(fileStream);
                return;
            }
            catch when (!backendProcess.HasExited)
            {
                // Keep polling until the backend route becomes available or the process exits.
            }
        }

        if (backendProcess.HasExited)
        {
            throw new InvalidOperationException(
                "OpenAPI export server exited before the document became available."
                + Environment.NewLine
                + string.Join(Environment.NewLine, outputBuffer)
            );
        }

        throw new TimeoutException($"Timed out waiting for {url}.");
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    private static bool IsHelp(string value) =>
        string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "help", StringComparison.OrdinalIgnoreCase);

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        WriteHelp();
        return 1;
    }

    private static void WriteHelp()
    {
        Console.WriteLine("Event.DevTasks");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  backend-format                         Format the backend solution.");
        Console.WriteLine(
            "  backend-unit-coverage                  Run backend unit tests with the coverage gate."
        );
        Console.WriteLine(
            "  backend-integration-tests              Run the backend integration test suite."
        );
        Console.WriteLine(
            "  backend-integration-endpoint-coverage  Audit controller endpoints against integration requests."
        );
        Console.WriteLine("  export-openapi                         Export backend OpenAPI JSON or YAML artifacts.");
        Console.WriteLine();
        WriteBackendFormatHelp();
        WriteBackendUnitCoverageHelp();
        WriteBackendIntegrationTestsHelp();
        WriteBackendIntegrationEndpointCoverageHelp();
        WriteExportOpenApiHelp();
    }

    private static void WriteBackendFormatHelp()
    {
        Console.WriteLine("backend-format options:");
        Console.WriteLine(
            "  --verify-no-changes   Fail instead of writing changes, matching CI format verification."
        );
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  dotnet run --project tools/Event.DevTasks -- backend-format --verify-no-changes");
        Console.WriteLine();
    }

    private static void WriteBackendUnitCoverageHelp()
    {
        Console.WriteLine("backend-unit-coverage options:");
        Console.WriteLine("  --threshold, -t   Minimum line coverage percentage. Default: 90");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  dotnet run --project tools/Event.DevTasks -- backend-unit-coverage --threshold 90");
        Console.WriteLine();
    }

    private static void WriteBackendIntegrationTestsHelp()
    {
        Console.WriteLine("backend-integration-tests options:");
        Console.WriteLine("  --filter          Optional dotnet test filter expression.");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  dotnet run --project tools/Event.DevTasks -- backend-integration-tests");
        Console.WriteLine();
    }

    private static void WriteBackendIntegrationEndpointCoverageHelp()
    {
        Console.WriteLine("backend-integration-endpoint-coverage options:");
        Console.WriteLine("  --fail-on-missing   Exit non-zero when uncovered controller actions are found.");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine(
            "  dotnet run --project tools/Event.DevTasks -- backend-integration-endpoint-coverage --fail-on-missing"
        );
        Console.WriteLine();
    }

    private static void WriteExportOpenApiHelp()
    {
        Console.WriteLine("export-openapi options:");
        Console.WriteLine(
            "  --output, -o   Output path relative to the repo root. Default: backend/openapi.yaml"
        );
        Console.WriteLine("  --port, -p     Temporary backend port used during export. Default: 8090");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --project tools/Event.DevTasks -- export-openapi");
        Console.WriteLine(
            "  dotnet run --project tools/Event.DevTasks -- export-openapi --output backend/openapi.json"
        );
        Console.WriteLine();
    }

    [GeneratedRegex(
        "public\\s+const\\s+string\\s+(?<name>\\w+)\\s*=\\s*\"(?<value>[^\"]*)\"",
        RegexOptions.Compiled
    )]
    private static partial Regex RouteConstantRegex();

    [GeneratedRegex("\\[Route\\((?<route>.+?)\\)\\]", RegexOptions.Compiled)]
    private static partial Regex RouteAttributeRegex();

    [GeneratedRegex(
        "\\[(?<attr>HttpGet|HttpPost|HttpPut|HttpDelete|HttpPatch)(?:\\((?<route>.+?)\\))?\\]",
        RegexOptions.Compiled
    )]
    private static partial Regex HttpAttributeRegex();

    [GeneratedRegex("class\\s+(?<name>\\w+Controller)\\b", RegexOptions.Compiled)]
    private static partial Regex ControllerClassRegex();

    [GeneratedRegex(
        "public\\s+(?:async\\s+)?(?:Task<[^>]+>|Task|IActionResult|ActionResult<[^>]+>|ActionResult)\\s+(?<method>\\w+)\\s*\\(",
        RegexOptions.Compiled
    )]
    private static partial Regex ControllerMethodRegex();

    [GeneratedRegex("^\"(?<literal>.*)\"$", RegexOptions.Compiled)]
    private static partial Regex QuotedLiteralRegex();

    [GeneratedRegex("^RoutePaths\\.(?<name>\\w+)$", RegexOptions.Compiled)]
    private static partial Regex RoutePathReferenceRegex();

    [GeneratedRegex("\\{[^}/:]+(?::[^}]+)?\\}", RegexOptions.Compiled)]
    private static partial Regex RouteParameterRegex();

    [GeneratedRegex("\\{[^}/]+\\}", RegexOptions.Compiled)]
    private static partial Regex SimpleRouteParameterRegex();

    [GeneratedRegex("/\\d+(?=/|$)", RegexOptions.Compiled)]
    private static partial Regex NumericRouteSegmentRegex();

    [GeneratedRegex("/[0-9a-f]{8}-[0-9a-f-]{27,35}(?=/|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex GuidRouteSegmentRegex();
}

internal sealed record PendingHttpRoute(string Verb, string ActionRoute, int AttributeLine);

internal sealed record EndpointDefinition(
    string Controller,
    string Action,
    string Verb,
    string Route,
    string Source,
    int Line
);

internal sealed record IntegrationRequest(string Verb, string Route, string Source);

internal sealed record EndpointCoverageResult(
    string Controller,
    string Action,
    string Verb,
    string Route,
    bool Covered,
    int MatchCount,
    string Source,
    int Line
);
