# Deployment

The repository supplies a Docker Compose development stack, application Dockerfiles, and a Kubernetes manifest/helper. These are starting points for deployment; they are not a complete production installation. See [setup](SETUP.md) for a local run and [configuration](CONFIGURATION.md) for settings.

## Docker Compose

From the repository root, prepare `.env` from `.env.example` without overwriting an existing file, then run:

```powershell
docker compose up --build -d
docker compose ps
docker compose logs -f backend event-indexer club-indexer clubpost-indexer media-worker
```

Compose provisions PostgreSQL with logical replication, Redis, Elasticsearch, Kafka, topic initialization, Kafka Connect, outbox connector registration, the API, frontend, and six workers. Application containers use service DNS names. Browser-facing URLs must be reachable from the user's browser, rather than container-only hostnames.

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

Use `docker compose logs` for API and worker diagnostics. Query a documented read endpoint to check API behavior and test an actual search update and hub connection. There is no dedicated API health endpoint mapped in the current entry point. Empty SMTP/Twilio settings disable their consumers, and empty storage settings idle the media worker; a running worker container does not prove messages are being delivered. See individual [worker READMEs](../backend/README.md#workers).

## Blob storage

Images live in two containers in the storage account named by `AZURE_STORAGE_CONNECTION_STRING`:

| Container | Setting | Access | Holds |
| --- | --- | --- | --- |
| Public | `AZURE_STORAGE_CONTAINER_NAME` | Anonymous read for blobs | Every image the application serves |
| Quarantine | `AZURE_STORAGE_QUARANTINE_CONTAINER_NAME` (default `event-assets-quarantine`) | Private | Browser uploads that have not been validated yet |

**The API does not create containers.** Create both before deploying a version that includes quarantine. `storage.quarantine` is on by default, and presigned uploads fail until the quarantine container exists. Use the DevTasks command with a connection string allowed to create containers:

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- storage-provision
```

Or use the Azure CLI:

```powershell
az storage container create --name <public-container> --public-access blob --connection-string "<connection-string>"
az storage container create --name event-assets-quarantine --public-access off --connection-string "<connection-string>"
```

The command reports containers that already exist without changing them. It fails if the quarantine container allows anonymous access, because that would publish unvalidated uploads.

At startup, the API checks that the containers it needs exist. That is the public one, plus the quarantine one while `storage.quarantine` is on. For any that are missing it logs an error naming this command; it keeps running, since everything except images still works. Until the containers exist, avatar uploads and attaches return 503, "Image storage is not available right now", rather than a 500. A browser upload into a missing quarantine container is refused by Azure itself.

Nothing on the request path needs container-create rights any more. The app still signs upload URLs with the account key, though, so its connection string cannot be narrowed below account-key access yet.

Browsers upload directly to the quarantine container. The storage account's Blob service CORS rules must allow `PUT` from the frontend origin with the `x-ms-blob-type` and `Content-Type` headers. Those rules are set for the whole account, so a configuration that already allowed uploads to the public container covers quarantine as well.

The API's hourly quarantine reaper deletes uploads that were never attached. As defence in depth you can also add a lifecycle management rule that deletes blobs in the quarantine container more than two days after creation. That does not interfere with the application, because an upload older than 24 hours can no longer be attached.

The `mediaassets` migration runs at startup like every other migration. It backfills one `Legacy` asset per existing image URL, without touching the blobs themselves.

### Validating in media-worker

By default the API validates an attached upload inside the attach request. Setting `FEATURE_STORAGE_QUARANTINE_INLINE=false` hands that work to [media-worker](../backend/src/worker/media-worker/README.md) instead, which is what Compose does. To switch a deployment over:

1. Create the `MEDIA_VALIDATION_*` request, DLQ and status topics. Compose's `kafka-init` creates them.
2. Deploy media-worker with Kafka, `AZURE_STORAGE_CONNECTION_STRING`, and both container names. It never connects to PostgreSQL, so the `mediaassets` migration only has to have run on the API.
3. Set the flag to false on the API. The API then consumes the status topic and runs the reconciler.

An attach waits up to ten seconds for the worker's verdict and otherwise returns 409, "still being checked". The asset stays Processing, and the API's reconciler re-drives it with backoff, parking it in NeedsReview after five unanswered claims. Watch the DLQ and the reconciler's log lines. A NeedsReview asset keeps its quarantined bytes for review.

**To roll back to inline validation**, set `FEATURE_STORAGE_QUARANTINE_INLINE=true`. Assets the worker left Processing are released once their claim is five minutes old, by the next attach of the same upload or by the hourly quarantine reaper, and are then validated inline.

**To roll back quarantine**, set `FEATURE_STORAGE_QUARANTINE=false`. Uploads then go straight to the public container and are checked in place, exactly as before. Uploads issued while quarantine was on but not yet attached need to be uploaded again. The reaper keeps running and deletes what quarantine held.

## Kubernetes assets and gaps

[`eventxperience.yml`](../eventxperience.yml) supplies namespace, service, deployment, and storage resources for a local cluster. [`bin/k8.ps1`](../bin/k8.ps1), exposed by `.\app.ps1 k8`, builds local images, applies the manifest, waits for core deployments, and starts port forwarding. It builds for `linux/arm64` and assumes those image tags are available to the target cluster; it does not publish them to a registry.

Review and adapt these assets before using them: the helper does not build the SMS or media worker images, the manifest has no media worker deployment, the manifest lacks the club indexer and the Compose Kafka Connect/outbox initialization path, credentials include development defaults/empty provider values, and frontend production SSR packaging and ingress/TLS are not supplied. Do not assume Kubernetes has functional parity with Compose. Complete image delivery, secret management, persistent storage, connector/topic provisioning, rollout probes, and infrastructure security for the intended environment as a separate implementation task.
