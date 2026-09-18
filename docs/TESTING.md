# Testing and coverage policy

Run backend commands from the repository root and frontend commands from `frontend/`. See the [unit-test README](../backend.tests.Unit/), [integration-test README](../backend.tests.Integration/), and [frontend README](../frontend/) for component entry points.

## Backend unit tests

```powershell
dotnet test backend.tests.Unit/backend.tests.Unit.csproj
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-unit-coverage
```

The unit project covers feature services/controllers/repositories with doubles, shared utilities, validation, security helpers, infrastructure policies, bootstrap behavior, and workers. Use xUnit, Moq, FluentAssertions, and existing builders/collections. Tests that change process environment or the global logger use the existing isolation collections.

### Backend coverage

The enforced default is **90% filtered unit line coverage**. The development task runs Release tests using `backend.coverage.runsettings` and the first-party Microsoft Code Coverage collector, reads Cobertura under `.tmp/backend-unit-coverage/`, and fails below the floor. Branch coverage is reported separately, without a separate branch floor.

The settings include backend/worker/indexer assemblies and exclude test assemblies, migrations/designers/model snapshots, seeders, and compiler/generated-code attributed code. The measured scope depends on assemblies actually loaded by the unit suite; a passing floor does not prove every standalone indexer is instrumented. Do not lower the threshold to accommodate a regression.

Prefer branch-focused behavior tests over assertion-free creation stubs. Use integration tests when correctness depends on PostgreSQL, Redis, Kafka, Elasticsearch, or real endpoint wiring.

## Backend integration tests

Start Docker before running:

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-tests
dotnet test backend.tests.Integration/backend.tests.Integration.csproj --filter "FullyQualifiedName~Features.Auth"
dotnet test backend.tests.Integration/backend.tests.Integration.csproj --filter "Category=EndToEnd"
```

The harness uses ASP.NET `WebApplicationFactory<Program>` in `Testing` with real Testcontainers PostgreSQL, Redis, Elasticsearch, and Kafka. Captcha, OAuth, and blob storage are faked; Kafka probes capture notification traffic. A running Docker daemon is required; starting the application's Compose stack is not necessary.

On Windows ARM64, Kafka-backed tests require an x64 .NET runtime/toolchain because the Confluent package's Windows native assets are x64/x86. Use a compatible execution environment; CI runs these tests on x64 Linux.

The harness allows up to four test classes in parallel, migrates a PostgreSQL template database, clones it for direct database tests, and pools four isolated API slots. Each slot separates database, Redis logical database, Elasticsearch indices, and Kafka topics. State is cleared before reuse; custom overrides run in a transient host on the leased slot. Reuse the fixtures and namespacing helpers instead of creating uncoordinated shared resources.

Feature endpoint tests live under `Features/`, seeder tests under `Seeders/`, and cross-feature business journeys under `Workflows/`. Workflow tests carry `Category=EndToEnd`; they exercise multiple users across API features, not the Angular UI. CI runs authentication, clubs, events, workflows, and other shards without duplicating the workflow category.

The application suppresses normal startup side effects under Testing; the harness owns migrations and infrastructure setup. Request rate limiting is skipped in Testing, so endpoint tests do not measure rate-limit enforcement.

### Endpoint audit

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-endpoint-coverage --fail-on-missing
```

This static audit compares discovered controller actions with matching API request literals in integration test sources. It reports covered/uncovered actions by controller; CI fails on missing matches. It does not execute tests or replace code coverage and can miss dynamically assembled requests. Add meaningful endpoint assertions rather than source literals solely to satisfy the audit.

PowerShell compatibility wrappers remain in `bin/backend-unit-coverage.ps1` and `bin/backend-integration-endpoint-coverage.ps1`.

## Frontend unit tests

From `frontend/`, install dependencies and generate the environment before tests:

```powershell
npm ci
npm run generate:env
npm test -- --watch=false --browsers=ChromeHeadlessCI --code-coverage
```

Colocated `*.spec.ts` tests use Karma/Jasmine through `@angular/build:karma` and `karma.conf.js`. Chrome/Chromium must be available. `ChromeHeadlessCI` uses no-sandbox/GPU/shared-memory flags suited to CI; ordinary `ChromeHeadless` is also available locally. Narrow a run with `--include="**/refresh.interceptor.spec.ts"`.

### Frontend coverage

With `--code-coverage`, Karma enforces **90% statements, lines, branches, and functions**. CI runs that command and uploads `frontend/coverage/`. Helpers under `src/testing/**` are excluded.

Karma instruments code reachable from specs; adding the first spec for a file can increase the denominator. Compare covered counts as well as percentages. Do not weaken floors or add assertion-free stubs to inflate results.

### Shared helpers and conventions

Import from `@testing`:

| Helper                                                                                    | Purpose                                                            |
| ----------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| `provideHttpTesting()`, `setupService()`                                                  | HTTP service setup with `httpMock`                                 |
| `envelope()`, `pascalEnvelope()`, `errorEnvelope()`                                       | Standard and compatibility API responses                           |
| `provideFeatureFlags()`                                                                   | Isolated service override; avoid mutating global environment flags |
| `provideTestStore()`, `dispatchSpy()`                                                     | NgRx user/session selector overrides and dispatch assertions       |
| `fakeActivatedRoute()`                                                                    | Consistent route snapshot and observables                          |
| `installMemoryStorage()`, `installThrowingStorage()`                                      | Storage doubles with restore functions                             |
| `flushPromises()`                                                                         | Drain asynchronous setup before HTTP expectations                  |
| `makeClub()`, `makeClubMember()`, `makeEventItem()`, `makeCurrentUser()`, `makeSession()` | Typed fixtures with partial overrides                              |

Helpers are excluded from the application compilation/bundle. New services, guards, interceptors, and normalizers need meaningful specs. Assert request URL/method/params/body and normalized results; cover supported camelCase/PascalCase payloads and missing/empty fields. Call `httpMock.verify()` in cleanup, restore replaced globals, and reset TestBed before configuring it twice in one spec.

## Frontend browser tests

From `frontend/`:

```powershell
npm run playwright:install
npm run test:e2e
```

Playwright tests live in `tests/` and use `playwright.config.ts`. They start `npm run start:e2e` at `http://127.0.0.1:3101` and can reuse a local server. The normal development port is 3090. The current home-page smoke test checks rendered UI; additional authenticated journeys require appropriate backend/test data.

The root `.mcp.json` configures Playwright; `frontend/.mcp.json` adds Angular CLI MCP. Launch frontend-focused tools from the Angular workspace so the CLI can resolve `angular.json`. Start the desired app server before navigating with browser tools. Equivalent frontend VS Code configuration is in `frontend/.vscode/mcp.json`.

See [CONTRIBUTING.md](../CONTRIBUTING.md) for formatting, type/build checks, audits, and PR validation expectations.
