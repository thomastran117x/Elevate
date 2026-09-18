# Setup

Run commands from the repository root unless a section states otherwise. See [configuration](CONFIGURATION.md) for settings and the [root README](../) for component links.

## Prerequisites

For the full container stack, install Docker with Compose v2 and start the Docker daemon. Linux containers are required. Verify with `docker version` and `docker compose version`. Allow enough memory for PostgreSQL, Redis, Elasticsearch, Kafka, and Kafka Connect.

For local application development, also install Node.js 24 or newer, npm, and the .NET SDK selected by [global.json](../global.json) (10.0.302 with latest-feature roll-forward). EF CLI operations require a compatible .NET 10 `dotnet-ef` tool. Chrome/Chromium is needed for Angular headless tests; Playwright installs its own browser binaries.

Examples use PowerShell. On other shells, use equivalent file-copy and environment-variable syntax; `dotnet`, npm, and Compose commands are otherwise shared.

## Environment files

Copy the tracked template only when the target does not exist:

```powershell
Copy-Item .env.example .env
```

The root template uses Compose service addresses and development defaults. Change provider configuration for the features you will exercise; do not commit real secrets. A basic page load does not establish that email, SMS, captcha, OAuth, payment, or uploads are configured.

The frontend generator reads `frontend/.env` from its working directory, not the root file. For local frontend work, copy the template there only if no file already exists:

```powershell
Copy-Item .env.example frontend/.env
```

Use the [configuration guide](CONFIGURATION.md) to align frontend public values/flags with the API. Existing process variables take precedence over dotenv values.

## Full Docker stack

```powershell
docker compose up --build -d
docker compose ps
docker compose logs -f backend
```

Normal API startup applies migrations automatically. Infrastructure, Kafka topics, and outbox connectors take time to initialize. Inspect service logs if startup fails. The frontend container runs Angular's development server.

| Service       | Host address                          | Address inside Compose    |
| ------------- | ------------------------------------- | ------------------------- |
| Frontend      | http://localhost:3090                 | http://frontend:3090      |
| API           | http://localhost:8090/api             | http://backend:8090/api   |
| PostgreSQL    | localhost:5433                        | postgres:5432             |
| Redis         | localhost:6380                        | redis:6379                |
| Elasticsearch | http://localhost:9200                 | http://elasticsearch:9200 |
| Kafka         | localhost:9093 (see limitation below) | kafka:9092                |
| Kafka Connect | http://localhost:8083                 | http://kafka-connect:8083 |

Stop containers with `docker compose down`; named volumes are retained. Do not delete volumes as a routine setup step.

## Local applications with infrastructure

You can start selected Compose infrastructure services while running applications locally:

```powershell
docker compose up -d postgres redis elasticsearch kafka kafka-init kafka-connect kafka-connect-init
```

For a host-run backend, adjust the root `.env` to host endpoints:

```dotenv
DB_CONNECTION_STRING=Host=localhost;Port=5433;Database=appdb;Username=appuser;Password=<POSTGRES_PASSWORD>
REDIS_URL=localhost:6380
ELASTICSEARCH_URL=http://localhost:9200
KAFKA_BOOTSTRAP_SERVERS=<host-reachable-kafka-bootstrap>
```

Replace placeholders; the database credentials must match your Compose PostgreSQL settings. **Kafka limitation:** the supplied broker advertises `kafka:9092`, even though its host port is 9093. Merely setting `KAFKA_BOOTSTRAP_SERVERS=localhost:9093` does not make broker metadata reachable from a host process. Configure a host-reachable advertised listener through a separate local override, or use a separately provisioned Kafka broker. The unchanged full container stack avoids that mismatch.

Restore and run the API:

```powershell
dotnet restore backend.sln
dotnet run --no-launch-profile --project backend/backend.csproj
```

The API defaults to 8090. It validates configuration, connects to PostgreSQL, applies migrations, and starts enabled hosted services. Optional seeding is controlled by `RUN_SEEDERS`.

From `frontend/`:

```powershell
npm ci
npm start
```

`npm start` generates the environment before serving on 3090. For same-origin API calls, set `BACKEND_URL=http://localhost:3090/api` in `frontend/.env`; the dev proxy defaults to `http://localhost:8090`. Export `API_PROXY_TARGET` into the process environment if overriding that proxy target. The generator's dotenv loading does not export variables back to the parent shell.

Run each desired worker using its project README. Application startup alone does not start the separate notification/indexer executables.

## Migrations and validation

The API applies committed migrations at startup. To manage migrations explicitly with the .NET 10 EF tool, run from the root:

```powershell
dotnet ef database update --project backend/backend.csproj --startup-project backend/backend.csproj
```

See [developer conventions](DEVELOPERS.md) before creating migrations and [testing](TESTING.md) for unit, integration, coverage, and browser checks.

## Existing helper limitations

`app.ps1` dispatches commands in `bin/`. Prefer the direct commands above for new setups:

- `env` generates older component-local settings and names; it is not equivalent to the current root `.env.example`.
- `setup` requires `backend/.env`, uses `npm install`, and assumes `dotnet-ef` and a reachable database are already available.
- `docker` checks for the legacy `docker-compose` executable before also invoking Compose v2.
- `local` opens Windows PowerShell terminals for the frontend, API, and workers; it does not provision infrastructure.
- `k8` has image/provisioning gaps described in [deployment](DEPLOYMENT.md).

If stale component `.env` files exist, inspect them: backend files nearer the executable can override values from the root file. Do not overwrite an existing environment file to troubleshoot.
