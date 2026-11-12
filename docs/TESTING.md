# Testing and coverage policy

Run backend commands from the repository root and frontend commands from `frontend/`. See the [unit-test README](../backend.tests.Unit/README.md), [integration-test README](../backend.tests.Integration/README.md), and [frontend README](../frontend/README.md) for component entry points.

## Backend unit tests

```powershell
dotnet test backend.tests.Unit/backend.tests.Unit.csproj
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-unit-coverage
```

The unit project covers feature services/controllers/repositories with doubles, shared utilities, validation, security helpers, infrastructure policies, bootstrap behavior, and workers. Use xUnit, Moq, FluentAssertions, and existing builders/collections. Tests that change process environment or the global logger use the existing isolation collections.

Auth code that records security events takes `IAuthSecurityEventEmitter`; pass `NullAuthSecurityEventEmitter.Instance` when a test does not care, or `RecordingAuthSecurityEventEmitter` from `Features/Auth/Events` to assert on what was emitted. `docs/SECURITY_EVENTS.md` is copied into the unit test output and checked against the event contract, so changing the catalog, reasons, operations, or metrics means updating that document in the same change.

Passkey tests use `SoftwareAuthenticator` (`backend.tests.Unit/Support`, linked into the integration project). It holds one ES256 key and answers the API's real options with a `none` attestation and signed assertions, so the shipped Fido2 library verifies the ceremonies rather than a mock. Its settings each break one thing a browser or authenticator would get right: the origin, the RP ID, user verification, the counter, the signature, or the user handle.

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

Feature endpoint tests live under `Features/`, seeder tests under `Seeders/`, and cross-feature business journeys under `Workflows/`. Workflow tests carry `Category=EndToEnd`; they exercise multiple users across API features, not the Angular UI. CI runs authentication, authorization, clubs, events, workflows, and other shards without duplicating the workflow category.

The application suppresses normal startup side effects under Testing; the harness owns migrations and infrastructure setup. Request rate limiting is skipped in Testing, so endpoint tests do not measure rate-limit enforcement.

### Endpoint audit

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-endpoint-coverage --fail-on-missing
```

This static audit compares discovered controller actions with matching API request literals in integration test sources. It reports covered/uncovered actions by controller; CI fails on missing matches. It does not execute tests or replace code coverage and can miss dynamically assembled requests. Add meaningful endpoint assertions rather than source literals solely to satisfy the audit.

### Authorization audit

CI runs the **Auth Contract Gate** job on every pull request. It fails when the authorization classification or the public/protected contract regresses.

`backend.tests.Unit/Application/Security/EndpointAuthorizationAuditTests.cs` reflects over the controllers and SignalR hubs that MVC and SignalR discover, and evaluates them with `EndpointAuthorizationGate`. It fails when:
- an action or hub method has neither `[Authorize]` nor `[AllowAnonymous]` on itself or its class;
- an `[Authorize]` action sits under a class-level `[AllowAnonymous]`;
- the anonymous surface differs from the reviewed list in `ReviewedPublicSurface.cs`, in either direction.

Negative fixtures in the same file omit metadata, conflict, or drift from the reviewed list, and prove that each case fails the gate.

`backend.tests.Unit/Application/OpenApi/OpenApiAuthorizationContractTests.cs` reads the committed `backend/openapi.json`. Every operation must carry `x-authorization`, protected operations must require `bearerAuth`, and the anonymous operations must equal the reviewed list. The gate job then regenerates the document and fails on any difference from the committed files:

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- export-openapi --output backend/openapi.json --port 8097
git diff --exit-code -- backend/openapi.json backend/openapi.yaml
```

Port 8097 matters: it is written into the document's server URL.

`backend.tests.Integration/Application/Security/` runs as its own `authorization` CI shard:
- `AuthorizationMatrixEndpointsTests.cs` repeats the metadata check against the running app's routed endpoints, including hubs and minimal endpoints.
- It sends an anonymous request to **every** protected endpoint and requires the `401` `UNAUTHORIZED` "Missing access token" envelope. It sends one to **every** public endpoint and requires that none of them answers with that challenge. The probes come from the routed endpoints themselves, not a hand-kept list.
- It also covers seeded public reads, the invalid-token and `403` envelopes, the `AdminOnly` policy against each global role, and club owner, manager, volunteer, unrelated-user, and admin access decided by the services.
- `RuntimeEndpointAuthorizationAuditTests.cs` is the runtime negative fixture. A throwaway host routes a minimal API and a controller without authorization metadata, and the gate must name both. It needs no containers.

### Monitoring definitions

CI's **Monitoring Definitions** job validates `docker/monitoring/` with the pinned images. It runs `promtool check config`, then `promtool test rules`, which injects representative series and asserts which auth alerts fire, then `amtool check-config`. It also runs `backend.tests.Unit/Application/Telemetry/`. Those tests check that:
- alert owners and runbook headings exist;
- every matched event and classification exists in the catalog;
- no rule or panel uses an identifying label;
- the dashboard covers every required area.

The local commands are in [monitoring](operations/monitoring.md#testing-the-rules).

### Auth drill

`auth-drill` in Event.DevTasks exercises session revocation, refresh-token reuse, password recovery, and the telemetry pipeline end to end, against a running non-production stack with Mailpit and Prometheus. It is run by hand, monthly; see [drills](operations/drills.md). It is not part of CI.

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

Playwright tests live in `tests/` and use `playwright.config.ts`. They start `npm run start:e2e` at `http://localhost:3101` and can reuse a local server. The normal development port is 3090. The default command skips the live authentication-abuse journey so it does not depend on an unprovisioned API.

To exercise login CAPTCHA escalation and signup/profile email availability, provision the backend from the repository root, then run the dedicated browser command from `frontend/`:

```powershell
$env:CORS_ORIGIN = "http://localhost:3101"
$env:CAPTCHA_ALLOW_BYPASS = "true"
$env:AUTH_SEED_ACCOUNT_BYPASS = "true"
$env:RUN_SEEDERS = "true"
docker compose up -d --build backend
Set-Location frontend
npm run test:e2e:auth-abuse
```

The backend command also starts its declared infrastructure dependencies. The dedicated Playwright configuration enables only `auth-abuse-protection.spec.ts`; that suite uses the seeded `avaparticipant` account and non-production CAPTCHA tokens through the frontend proxy.

`account-activity.spec.ts` follows the account security history end to end: it signs in, opens **Account settings → Activity** from the keyboard, and waits for the sign-in entry. It needs the same backend settings and a running security-history worker (`docker compose up -d --build backend security-history-worker`). Run it from `frontend/`:

```powershell
$env:PLAYWRIGHT_LIVE_AUTH = "true"
npx playwright test tests/account-activity.spec.ts
```

`account-recovery-codes.spec.ts` needs the same backend settings, because the seeded account's step-up bypass opens the Security tab. It generates recovery codes on **Account settings → Security**, checks that they are shown once, copied, downloaded, and cleared after the user confirms they saved them, and then drives the sign-in verification page's recovery-code option against mocked challenge responses. Running it replaces the seeded account's recovery codes. Run it with `npx playwright test tests/account-recovery-codes.spec.ts` and the same `PLAYWRIGHT_LIVE_AUTH` setting.

`account-linked-providers.spec.ts` needs no backend. It mocks the API and opens the shared provider callback pages as Google would redirect to them. It covers a completed link, a declined consent, an identity linked to another account, an expired request, unlinking with the last-method guard, and the link a sign-in proposes when its email matches an existing account. It never contacts Google or Microsoft. Run it with `npx playwright test tests/account-linked-providers.spec.ts`.

`account-devices.spec.ts` also mocks the API and needs no backend. On **Account settings → Devices** it checks that both lists load without a step-up and mark this device. It signs out another session after an in-place step-up that then finishes the sign-out, signs out everywhere back to the sign-in page, and forgets a trusted device. Run it with `npx playwright test tests/account-devices.spec.ts`.

`account-passkeys.spec.ts` mocks the API and runs real WebAuthn ceremonies in Chromium's virtual authenticator, added through the DevTools protocol. It adds a passkey on the Security tab, signs in with the passkey button and from the username field's autofill, keeps the password form after a refused prompt, steps up with a passkey, and removes one. It checks the credential JSON the page sends (type, origin, and user handle), while the backend integration tests cover the server's verification. Run it with `npx playwright test tests/account-passkeys.spec.ts`.

The root `.mcp.json` configures Playwright; `frontend/.mcp.json` adds Angular CLI MCP. Launch frontend-focused tools from the Angular workspace so the CLI can resolve `angular.json`. Start the desired app server before navigating with browser tools. Equivalent frontend VS Code configuration is in `frontend/.vscode/mcp.json`.

See [CONTRIBUTING.md](../CONTRIBUTING.md) for formatting, type/build checks, audits, and PR validation expectations.
