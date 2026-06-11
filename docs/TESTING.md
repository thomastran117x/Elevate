# Testing

Backend tests are split into two projects:

- `backend.tests.Unit`
- `backend.tests.Integration`

The unit suite covers controller logic, pure helpers, token/auth logic, and worker parsers.
The integration suite covers SQLite-backed services/repositories/outbox writers, seeders, and HTTP auth flows through the real ASP.NET app running in the `Testing` environment.

## Commands

Run the unit suite:

```powershell
dotnet test backend.tests.Unit\backend.tests.Unit.csproj
```

Run the unit suite with the enforced backend coverage gate:

```powershell
.\bin\backend-unit-coverage.ps1
```

Run the integration suite:

```powershell
dotnet test backend.tests.Integration\backend.tests.Integration.csproj
```

Run the full backend test pass:

```powershell
.\bin\backend-unit-coverage.ps1
dotnet test backend.tests.Integration\backend.tests.Integration.csproj
```

Run only the auth integration flow coverage:

```powershell
dotnet test backend.tests.Integration\backend.tests.Integration.csproj --filter "FullyQualifiedName~backend.tests.Integration.Features.Auth.AuthEndpointsTests"
```

Run backend unit coverage with generated code and seed data excluded from the count:

```powershell
.\bin\backend-unit-coverage.ps1
```

The coverage script:

- runs `backend.tests.Unit` in `Release`
- uses `backend.coverage.runsettings`
- reads the generated Cobertura report from a repo-local `.tmp` directory
- fails if filtered backend unit line coverage is below `90.00%`

The filtered coverage scope keeps application code such as services, controllers, repositories, utilities, and DTO/contracts in scope, while excluding:

- EF Core migrations and designer files
- `src/main/seeders/**`
- compiler/generated-code attributed files

The current backend coverage improvement plan lives in:

- `docs/COVERAGE_ROADMAP.md`

## Test Structure

- `backend.tests.Unit/Features`
- `backend.tests.Unit/Workers`
- `backend.tests.Integration/Features`
- `backend.tests.Integration/Seeders`

Auth integration tests use:

- ASP.NET `WebApplicationFactory<Program>`
- SQLite in-memory database
- in-memory fake cache
- fake captcha provider
- fake OAuth provider
- captured publisher for email/token assertions

The backend app exposes a `Testing` startup path so integration tests can boot without running production-only startup side effects such as live Redis wiring, database migration checks, or hosted background workers.
