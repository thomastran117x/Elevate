# Backend unit tests

.NET 10 xUnit suite with Moq and FluentAssertions. Tests cover feature behavior, shared contracts/utilities, validation/security, infrastructure policies, bootstrap code, and workers using isolated doubles. Docker is not required for the unit suite.

Commands run from the repository root with the SDK selected by `global.json`:

```powershell
dotnet test backend.tests.Unit/backend.tests.Unit.csproj
dotnet test backend.tests.Unit/backend.tests.Unit.csproj --filter "FullyQualifiedName~Workers"
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-unit-coverage
```

Use `Features/`, `Infrastructure/`, `Application/`, `Shared/`, and `Workers/` to locate relevant tests. Reuse builders in `Support/`. Tests changing process environment or the shared logger belong in the corresponding isolation collections; restore state in cleanup.

The project references the API, email worker, and SMS worker. Indexer parser sources are linked into the test project with test envelope types, so parser coverage does not imply execution of every standalone indexer host.

The coverage task runs Release tests with the first-party collector and [runsettings](../backend.coverage.runsettings), writes Cobertura below `.tmp/backend-unit-coverage/`, and enforces 90% filtered line coverage by default. Branch coverage is reported. See [testing and coverage policy](../docs/TESTING.md) for exclusions and scope limitations.

Keep assertions focused on behavior and meaningful branches. Use [integration tests](../backend.tests.Integration/README.md) when correctness depends on PostgreSQL or real API/infrastructure wiring. Follow [CONTRIBUTING](../CONTRIBUTING.md).
