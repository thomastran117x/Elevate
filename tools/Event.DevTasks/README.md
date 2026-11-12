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
| `storage-provision`                     | `--connection-string`, `--container`, `--quarantine-container`                     | Creates the public blob container (anonymous blob read) and the private quarantine container. Settings default to the matching `AZURE_STORAGE_*` variables, read from the environment and then the root `.env`. The quarantine container defaults to `event-assets-quarantine`. Existing containers are reported without being changed. The command fails if the quarantine container allows anonymous access. |
| `auth-drill`                            | `--base-url`, `--mailpit-url` (required); `--prometheus-url`, `--environment`, `--scenario`, `--report`, `--confirm-non-production` | Runs the session-revocation, token-reuse, recovery, and telemetry drills against a running non-production API, with a throwaway account whose email Mailpit catches. Writes a JSON report (default `.tmp/auth-drill/<run>.json`) of step outcomes, timings, and request ids, never credentials or tokens. Deletes the account at the end. Fails on any step failure, and refuses hosts that do not look non-production without the confirmation flag. See [drills](../../docs/operations/drills.md). |

Every command supports help. Unknown commands/options, missing values, invalid numeric inputs, subprocess failures, and task exceptions return nonzero. Coverage also fails for missing/invalid reports or coverage below the selected threshold. The endpoint audit only fails on missing actions when its flag is supplied.

## Examples

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-format --verify-no-changes
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-unit-coverage
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-tests --filter "Category=EndToEnd"
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-endpoint-coverage --fail-on-missing
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- export-openapi --output backend/openapi.yaml --port 8091
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- storage-provision
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- auth-drill --base-url http://localhost:8090 --mailpit-url http://localhost:8025 --prometheus-url http://localhost:9090
```

## OpenAPI export details

Relative output paths resolve against the repository root; absolute paths are also accepted. Supported extensions are `.json`, `.yaml`, and `.yml`. Selecting any supported format writes both JSON and YAML counterparts. Unsupported extensions fail.

Use an unused port. The committed documents are exported on port 8097, which is written into their server URL, and the CI Auth Contract Gate regenerates them on that port and fails on any difference. Regenerate them with `--output backend/openapi.json --port 8097`. The task builds the backend, runs it in Development with `OPENAPI_EXPORT=true`, clears `OPENAPI_INCLUDE_PREFIX`, and sets its document server URL. Export mode skips startup validation/migrations/seeders/hosted services and uses SQLite plus a no-op cache, so application infrastructure containers are unnecessary. Feature flags still affect discovery; inspect the generated diff.

Build, host startup, document download, and port-binding failures produce a nonzero result; outputs may be incomplete on failure. Do not replace committed artifacts with partial output.

See [testing](../../docs/TESTING.md), [API documentation](../../docs/API.md), and [CONTRIBUTING](../../CONTRIBUTING.md). PowerShell wrappers in `bin/` retain compatibility for formatting, coverage, and export entry points.
