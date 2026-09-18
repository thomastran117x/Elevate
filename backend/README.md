# Backend API

ASP.NET Core application targeting .NET 10, with EF Core/PostgreSQL, Redis, Elasticsearch, Kafka, JWT/CSRF/MFA, SignalR, and generated OpenAPI. The executable entry point is `src/main/Program.cs`.

Feature controllers, services, repositories, and contracts live under `src/main/features/`. `application/` configures DI, middleware, security, routes, flags, and OpenAPI; `infrastructure/` integrates external stores; `shared/` holds cross-feature contracts/utilities. EF migrations are in `Migrations/`.

See [architecture](../docs/ARCHITECTURE.md), [setup](../docs/SETUP.md), [configuration](../docs/CONFIGURATION.md), and [CONTRIBUTING](../CONTRIBUTING.md). All commands below run from the repository root.

## Run and build

```powershell
dotnet restore backend.sln
dotnet build backend/backend.csproj
dotnet run --no-launch-profile --project backend/backend.csproj
```

Use the SDK selected by `global.json`. Provision PostgreSQL, Redis, Elasticsearch, and reachable Kafka; provider-backed features need their own credentials. Root `.env.example` targets the container network. For host execution, use the host ports in setup and account for Kafka's advertised-listener limitation.

Normal startup validates settings, verifies database connectivity, applies committed migrations, and optionally seeds data. API startup does not start the five separate worker executables. `PORT` defaults to 8090; controllers are under `/api` and the club hub is at `/api/hubs/clubs`.

Local `.env` loading walks executable ancestors; nearer files can override root settings, while existing nonempty process values are preserved. Containers rely on injected environment values. See configuration before retaining older `backend/.env` files.

## Migrations and API reference

```powershell
dotnet ef database update --project backend/backend.csproj --startup-project backend/backend.csproj
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- export-openapi --port 8091
```

Explicit EF operations require a compatible .NET 10 EF CLI tool. Follow [developer conventions](../docs/DEVELOPERS.md) for creating migrations.

The export task writes both [openapi.json](openapi.json) and [openapi.yaml](openapi.yaml). Live routes include `/openapi.json` and `/openapi.yaml`; see the [API guide](../docs/API.md) for envelopes, security, flags, and hub behavior.

## Validation

```powershell
dotnet format backend.sln --verify-no-changes
dotnet test backend.tests.Unit/backend.tests.Unit.csproj
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-unit-coverage
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-tests
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-endpoint-coverage --fail-on-missing
```

Integration tests require Docker; unit coverage defaults to a 90% filtered line floor. See [testing](../docs/TESTING.md), [unit tests](../backend.tests.Unit/README.md), and [integration tests](../backend.tests.Integration/README.md).

## Workers

Each worker has its own project and Dockerfile; container builds use `backend/` as the context.

- [Event indexer](src/worker/event-indexer/README.md)
- [Club indexer](src/worker/club-indexer/README.md)
- [Club-post indexer](src/worker/clubpost-indexer/README.md)
- [Email worker](src/worker/email-worker/README.md)
- [SMS worker](src/worker/sms-worker/README.md)

Indexers consume PostgreSQL outbox CDC via Kafka Connect. Notification workers consume API-published email/SMS messages; the email worker publishes event invitation delivery status for the API consumer. See [deployment](../docs/DEPLOYMENT.md) for stack initialization and current Kubernetes gaps.
