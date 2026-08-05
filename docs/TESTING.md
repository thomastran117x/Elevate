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

## Frontend Unit Tests (Karma + Jasmine)

Angular unit tests live beside the code they cover as `*.spec.ts` under `frontend/src`. They run on Karma + Jasmine through the `@angular/build:karma` builder, configured by `frontend/karma.conf.js`.

`npm run generate:env` must run first — it writes `src/environments/environment.ts`, which most specs import.

```powershell
cd frontend
npm run generate:env
npm test -- --watch=false --browsers=ChromeHeadlessCI
```

Add `--code-coverage` for an HTML report in `frontend/coverage/` plus a summary in the console. Narrow a run with `--include`:

```powershell
npm test -- --watch=false --browsers=ChromeHeadlessCI --include="**/refresh.interceptor.spec.ts"
```

`ChromeHeadlessCI` is a custom launcher (`--no-sandbox --disable-gpu --disable-dev-shm-usage`) that also works on GitHub runners, where plain `ChromeHeadless` cannot start its sandbox. Plain `ChromeHeadless` still works locally.

### Shared test helpers (`frontend/src/testing/`)

Import from `@testing` rather than repeating TestBed boilerplate:

| Helper | Use |
| --- | --- |
| `provideHttpTesting()` / `setupService(Token, extra?)` | HTTP-backed service specs; `setupService` returns `{ service, httpMock }` |
| `envelope(data, overrides?)`, `pascalEnvelope(Data)`, `errorEnvelope(code, message)` | Response bodies in the `{ success, message, data, error, meta }` contract |
| `provideFeatureFlags({ auth: false })` | Overrides `FeatureFlagsService` — **use this instead of mutating `environment.featureFlags`**, which is module-global and leaks between specs |
| `provideTestStore({ user, session })`, `dispatchSpy(store)` | NgRx `MockStore` with `selectUser` / `selectSession` / `selectAccessToken` pre-overridden |
| `fakeActivatedRoute({ params, queryParams })` | `ActivatedRoute` double whose observables and `snapshot` stay in sync |
| `installMemoryStorage(kind, seed?)`, `installThrowingStorage(kind)` | Swap `localStorage` / `sessionStorage`; both return a restore function for `afterEach` |
| `flushPromises()` | Drain microtasks before `httpMock.expectOne` when the service `await`s something first (e.g. CSRF bootstrap) |
| `makeClub()`, `makeClubMember()`, `makeEventItem()`, `makeCurrentUser()`, `makeSession()` | Fully-populated fixtures with partial overrides |

`src/testing/**` is excluded from `tsconfig.app.json`, so helpers may use Jasmine types and never reach the app bundle.

### Conventions

- New services, guards, interceptors and normalizer functions ship with a spec.
- Assert on the request (URL, method, serialized params, body) and on the normalized result, not on internals.
- Cover both the camelCase and PascalCase payload shapes for anything with `??` fallback chains — that is where silent regressions hide.
- Call `httpMock.verify()` in `afterEach`.

## Frontend E2E (Playwright)

Frontend end-to-end tests live in `frontend/tests/` and use Playwright (`frontend/playwright.config.ts`). Playwright auto-starts the E2E dev server (`npm run start:e2e`, served at `http://127.0.0.1:3101`, matching the config `baseURL`). The regular dev server runs at `http://localhost:3090` (`npm start`).

```powershell
cd frontend
npm run playwright:install   # first time only, installs browsers
npm run test:e2e
```

### MCP servers (for Claude Code / agents)

Two `.mcp.json` files register MCP servers for Claude Code so agents can drive a real browser (and query the Angular workspace) through MCP tools instead of writing one-off scripts. VS Code uses the equivalent `frontend/.vscode/mcp.json`.

- **Repo-root `.mcp.json`** — the **Playwright** MCP server (`browser_navigate`, `browser_snapshot`, etc.). Loaded when you launch Claude Code from the repo root. Browser automation needs no project context, so it works from anywhere. Runs headless by default; remove the `--headless` flag to watch a visible browser.
- **`frontend/.mcp.json`** — **Playwright + Angular CLI** (`ng mcp`). Loaded when you launch Claude Code from `frontend/`.

Why the Angular CLI MCP lives only in `frontend/.mcp.json`: Claude Code launches stdio MCP servers with their working directory set to wherever you started Claude Code, and it [ignores the `cwd` field](https://github.com/anthropics/claude-code/issues/17565). The Angular CLI MCP must run inside the Angular workspace (`frontend/`) to resolve the local `@angular/cli` and read `angular.json`, so it only works when Claude Code is launched from `frontend/`. For frontend-focused agent work, run `cd frontend && claude`.

To exercise a running app via the Playwright MCP, start `npm run start:e2e` first (serves `http://127.0.0.1:3101`), then point the MCP browser at that URL.
