# Backend integration tests

.NET 10 xUnit suite exercising real API wiring, repositories/services, seeders, and business workflows. Tests use ASP.NET `WebApplicationFactory<Program>` with Testcontainers PostgreSQL, Redis, Elasticsearch, and Kafka. Captcha/OAuth/blob storage are faked; notification assertions use Kafka probes.

## Prerequisites and commands

Run from the repository root with the SDK selected by `global.json` and Docker running. The fixture provisions its own containers; the application Compose stack is not required.

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-tests
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-tests --filter "FullyQualifiedName~Features.Events"
dotnet test backend.tests.Integration/backend.tests.Integration.csproj --filter "Category=EndToEnd"
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-endpoint-coverage --fail-on-missing
```

Windows ARM64 requires a compatible x64 .NET execution environment for Kafka's native Windows assets. CI uses x64 Linux runners.

## Fixtures and layout

`Infrastructure/` contains container fixtures, database cloning, namespacing, the API app pool, test doubles, and Kafka probes. Up to four test classes run in parallel. A migrated template database supports clones; four API slots separate database, Redis, Elasticsearch, and Kafka resources. Slots clear state before reuse; custom overrides use transient hosts.

Reuse the provided fixtures and resource namespace. Avoid cross-test assumptions about rows, indices, topics, shared hosts, or ordering. `Testing` suppresses normal startup migrations/seeders/hosted services; the harness sets up infrastructure. Normal request rate limiting is skipped, so those budgets need separate verification.

`Features/` contains endpoint and persistence tests, `Seeders/` verifies thematic data creation, and `Workflows/` follows users across complete business journeys. Workflows are tagged `Category=EndToEnd`; they drive API calls rather than the Angular UI. CI shards authentication, clubs, events, workflows, and other tests.

The endpoint audit is static source matching, not runtime coverage; it reports actions without a matching request and fails with `--fail-on-missing`. See [testing](../docs/TESTING.md) for coverage policy and [CONTRIBUTING](../CONTRIBUTING.md) for checks.
