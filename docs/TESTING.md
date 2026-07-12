# Testing

Backend tests are split into two projects:

- `backend.tests.Unit`
- `backend.tests.Integration`

The unit suite covers controller logic, pure helpers, token/auth logic, and worker parsers.
The integration suite now runs the real ASP.NET app in the `Testing` environment against Docker-backed Testcontainers for MySQL, Redis, Elasticsearch, and Kafka, covering repository/service flows, seeders, search, and HTTP auth/event/club endpoints.

## Commands

Run the unit suite:

```powershell
dotnet test backend.tests.Unit\backend.tests.Unit.csproj
```

Run the unit suite with the enforced backend coverage gate:

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-unit-coverage
```

Run the integration suite:

Docker must be running locally before you start the integration suite.
On Windows ARM64, Kafka-backed integration tests also require an x64 .NET installation because `Confluent.Kafka` currently restores Windows native assets for x64/x86, not ARM64.

```powershell
dotnet test backend.tests.Integration\backend.tests.Integration.csproj
```

Run the full backend test pass:

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-unit-coverage
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-tests
```

Run only the auth integration flow coverage:

```powershell
dotnet test backend.tests.Integration\backend.tests.Integration.csproj --filter "FullyQualifiedName~backend.tests.Integration.Features.Auth.AuthEndpointsTests"
```

Run backend unit coverage with generated code and seed data excluded from the count:

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-unit-coverage
```

Run the integration endpoint audit:

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-endpoint-coverage
```

Compatibility shims still exist if you prefer the older PowerShell entrypoints:

- `.\bin\backend-unit-coverage.ps1`
- `.\bin\backend-integration-endpoint-coverage.ps1`

The coverage script:

- runs `backend.tests.Unit` in `Release`
- uses `backend.coverage.runsettings`
- reads the generated Cobertura report from a repo-local `.tmp` directory
- fails if filtered backend unit line coverage is below `90.00%`

The filtered coverage scope keeps application code such as services, controllers, repositories, utilities, and DTO/contracts in scope, while excluding:

- EF Core migrations and designer files
- `src/main/seeders/**`
- compiler/generated-code attributed files

The integration endpoint audit is a separate metric. It reports the percentage of controller actions that have at least one matching `/api/...` request in the integration test sources. It is useful for endpoint surface coverage, but it is not a substitute for backend code coverage.

The current backend coverage improvement plan lives in:

- `docs/COVERAGE_ROADMAP.md`

## Test Structure

- `backend.tests.Unit/Features`
- `backend.tests.Unit/Workers`
- `backend.tests.Integration/Features`
- `backend.tests.Integration/Seeders`

Auth integration tests use:

- ASP.NET `WebApplicationFactory<Program>`
- Testcontainers-backed MySQL, Redis, Elasticsearch, and Kafka
- fake captcha provider
- fake OAuth provider
- fake blob storage
- Kafka-backed test probes for email and SMS assertions

The backend app exposes a `Testing` startup path so integration tests can boot with real infra wiring while still avoiding production-only side effects such as background email/SMS workers. A running Docker daemon is now a hard requirement for `backend.tests.Integration` locally and in CI.

## Frontend E2E (Playwright)

Frontend end-to-end tests live in `frontend/tests/` and use Playwright (`frontend/playwright.config.ts`). Playwright auto-starts the E2E dev server (`npm run start:e2e`, served at `http://127.0.0.1:3101`, matching the config `baseURL`). The regular dev server runs at `http://localhost:3090` (`npm start`).

```powershell
cd frontend
npm run playwright:install   # first time only, installs browsers
npm run test:e2e
```

### Playwright MCP (for Claude Code / agents)

The repo-root `.mcp.json` registers the Playwright MCP server for Claude Code, so agents can drive a real browser through MCP tools (`browser_navigate`, `browser_snapshot`, etc.) instead of writing one-off Playwright scripts. It runs headless by default; remove the `--headless` flag in `.mcp.json` to watch a visible browser. VS Code uses the equivalent `frontend/.vscode/mcp.json`. To exercise a running app, start `npm run start:e2e` first, then point the MCP browser at `http://127.0.0.1:3101`.
