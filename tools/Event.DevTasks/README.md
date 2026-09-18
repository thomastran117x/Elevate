# Event.DevTasks

.NET 10 command-line utility for repeatable repository tasks. It locates the repository from its executable ancestors. Use the SDK selected by `global.json`; commands below run from the repository root.

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- --help
```

## Commands

| Command                                 | Options                                                                            | Behavior and outputs                                                                                                                                                                                     |
| --------------------------------------- | ---------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `backend-format`                        | `--verify-no-changes`                                                              | Restores the solution, then runs dotnet format. Without the option it rewrites files; with it formatting discrepancies fail.                                                                             |
| `backend-unit-coverage`                 | `--threshold` / `-t` (default 90)                                                  | Clears its previous results under `.tmp/backend-unit-coverage/`, runs Release unit coverage, reads Cobertura, reports line/branch coverage, and enforces the line floor.                                 |
| `backend-integration-tests`             | `--filter`                                                                         | Runs the integration project in Release, optionally with a dotnet test filter; requires Docker.                                                                                                          |
| `backend-integration-endpoint-coverage` | `--fail-on-missing`                                                                | Statically compares controller actions with integration request literals, prints a report by controller and missing actions, and optionally fails on uncovered actions. No containers/tests are started. |
| `export-openapi`                        | `--output` / `-o` (default `backend/openapi.yaml`), `--port` / `-p` (default 8090) | Builds the API, starts a temporary export-mode host, writes paired JSON/YAML documents, and stops the host.                                                                                              |

Every command supports help. Unknown commands/options, missing values, invalid numeric inputs, subprocess failures, and task exceptions return nonzero. Coverage also fails for missing/invalid reports or coverage below the selected threshold. The endpoint audit only fails on missing actions when its flag is supplied.

## Examples

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-format --verify-no-changes
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-unit-coverage
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-tests --filter "Category=EndToEnd"
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-endpoint-coverage --fail-on-missing
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- export-openapi --output backend/openapi.yaml --port 8091
```

## OpenAPI export details

Relative output paths resolve against the repository root; absolute paths are also accepted. Supported extensions are `.json`, `.yaml`, and `.yml`. Selecting any supported format writes both JSON and YAML counterparts. Unsupported extensions fail.

Use an unused port. The task builds the backend, runs it in Development with `OPENAPI_EXPORT=true`, clears `OPENAPI_INCLUDE_PREFIX`, and sets its document server URL. Export mode skips startup validation/migrations/seeders/hosted services and uses SQLite plus a no-op cache, so application infrastructure containers are unnecessary. Feature flags still affect discovery; inspect the generated diff.

Build, host startup, document download, and port-binding failures produce a nonzero result; outputs may be incomplete on failure. Do not replace committed artifacts with partial output.

See [testing](../../docs/TESTING.md), [API documentation](../../docs/API.md), and [CONTRIBUTING](../../CONTRIBUTING.md). PowerShell wrappers in `bin/` retain compatibility for formatting, coverage, and export entry points.
