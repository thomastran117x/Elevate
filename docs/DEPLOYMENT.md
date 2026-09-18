# Deployment

The repository supplies a Docker Compose development stack, application Dockerfiles, and a Kubernetes manifest/helper. These are starting points for deployment; they are not a complete production installation. See [setup](SETUP.md) for a local run and [configuration](CONFIGURATION.md) for settings.

## Docker Compose

From the repository root, prepare `.env` from `.env.example` without overwriting an existing file, then run:

```powershell
docker compose up --build -d
docker compose ps
docker compose logs -f backend event-indexer club-indexer clubpost-indexer
```

Compose provisions PostgreSQL with logical replication, Redis, Elasticsearch, Kafka, topic initialization, Kafka Connect, outbox connector registration, the API, frontend, and five workers. Application containers use service DNS names. Browser-facing URLs must be reachable from the user's browser, rather than container-only hostnames.

The API listens on 8090 and applies migrations before serving normal traffic. A database connection or migration failure terminates startup. Review migrations and back up persistent data before upgrades; startup migration execution is part of the current implementation and must be accounted for when scaling API replicas. Keep seeding and auth seed-account bypass disabled outside controlled demos.

Named Compose volumes retain infrastructure state. `docker compose down` stops the stack without deleting them. Volume deletion is a deliberate data reset and should not be part of a routine upgrade or troubleshooting command.

## Built frontend and SSR

The supplied frontend Dockerfile launches `ng serve`. For built SSR serving, run from `frontend/` after installing dependencies and supplying the intended public build configuration:

```powershell
npm run generate:env
npm run build
npm run serve:ssr:frontend
```

The server entry is `dist/frontend/server/server.mjs`. Set `PORT` (default 3090), `API_PROXY_TARGET` (backend origin), and `NG_ALLOWED_HOSTS` (served hostnames). The Express server serves the browser output, proxies `/api`, and tunnels API WebSockets. A production frontend image/process supervisor must run this built server; it is not provided by the current development Dockerfile.

Public environment values and frontend flags are baked into the build. Rebuild for changes to them. Set `NODE_ENV=production` during environment generation for the generated production flag. Changing runtime server variables does not regenerate the browser bundle.

## Production configuration and operations

- Supply deployment-specific JWT signing secrets, a valid TOTP encryption key, provider credentials, and trusted proxy/CORS/host settings. Keep server secrets out of frontend build configuration and source control.
- Terminate TLS through the chosen ingress/reverse proxy, configure forwarded headers to match it, and preserve cookies, authorization headers, and WebSocket upgrades. `/api/hubs/clubs` also requires support for long-lived connections.
- Coordinate shared feature flags between the backend and frontend build. Verify flags absent from Compose mappings explicitly rather than assuming every backend option is forwarded.
- Provision persistent storage and backups for authoritative database data and the infrastructure state you intend to retain. Monitor database connectivity, connector state, Kafka consumer lag and DLQs, Elasticsearch updates, notification delivery, API error rates, and logs.

Use `docker compose logs` for API and worker diagnostics. Query a documented read endpoint to check API behavior and test an actual search update and hub connection. There is no dedicated API health endpoint mapped in the current entry point. Empty SMTP/Twilio settings disable their consumers; a running worker container does not prove messages are being delivered. See individual [worker READMEs](../backend/README.md#workers).

## Kubernetes assets and gaps

[`eventxperience.yml`](../eventxperience.yml) supplies namespace, service, deployment, and storage resources for a local cluster. [`bin/k8.ps1`](../bin/k8.ps1), exposed by `.\app.ps1 k8`, builds local images, applies the manifest, waits for core deployments, and starts port forwarding. It builds for `linux/arm64` and assumes those image tags are available to the target cluster; it does not publish them to a registry.

Review and adapt these assets before using them: the helper does not build the SMS image, the manifest lacks the club indexer and the Compose Kafka Connect/outbox initialization path, credentials include development defaults/empty provider values, and frontend production SSR packaging and ingress/TLS are not supplied. Do not assume Kubernetes has functional parity with Compose. Complete image delivery, secret management, persistent storage, connector/topic provisioning, rollout probes, and infrastructure security for the intended environment as a separate implementation task.
