# Auth service health and telemetry loss

| Alert | Severity | Owner |
| --- | --- | --- |
| [`AuthFaultRate`](#authfaultrate) | page | Auth on-call |
| [`AuthEventPipelineBroken`](#autheventpipelinebroken) | page | Auth on-call |
| [`AuthSecurityHistoryNotRecording`](#authsecurityhistorynotrecording) | ticket | Auth on-call |
| [`AuthTelemetryAbsent`](#authtelemetryabsent) | ticket | Auth on-call |

Owners and escalation are defined in [ownership](../ownership.md). These alerts are about the controls themselves: if they fail, the other alerts go blind or the audit trail has gaps.

## AuthFaultRate

**Meaning:** more than 2% of auth requests (sign-in, OAuth, refresh, step-up, recovery, verification, factor change) answered with a server fault for 10 minutes, with at least 0.05/s of auth traffic. Faults are service errors, not refusals.

### Triage

1. In the **Latency and faults** row, find which `operation` is faulting and whether latency rose first. A latency rise first suggests a slow dependency.
2. Read the API log for exceptions around that time, and check PostgreSQL, Redis, and the OAuth providers:

   ```sh
   docker compose logs --since 30m backend | grep -E 'ERR|FTL|Exception' | head -50
   docker compose ps postgres redis
   ```

### Contain

Restore the failing dependency, or roll back the release. A Redis outage fails sign-in throttling open to per-replica limits; see [abuse protection](../../CONFIGURATION.md#authentication-abuse-protection).

### Verify and close

Record the operation, cause, window, and fix.

## AuthEventPipelineBroken

**Meaning:** a security event sink (`logging`, `metrics`, or `security_history`) has thrown for 5 minutes. The events it should have written are lost to that destination. Requests are unaffected, by design.

### Triage

The emitter logs the sink and the exception **type** (never its message) as event 33901:

```sh
docker compose logs --since 30m backend | grep 'sink' | grep -i 'fail' | head
```

`security_history` failing usually means the in-process queue or outbox writer is broken; see the next section.

### Contain

Fix the failing sink's dependency, or roll back. As a last resort, a sink can be switched off without a restart (`Auth__SecurityEvents__LogSinkEnabled` / `MetricsSinkEnabled`; `Auth__SecurityHistory__PublishEnabled`). Doing so stops recording entirely, so note it in the incident record.

### Verify and close

Record which sink failed, for how long, the cause, and the estimated number of lost events: the `auth.security_events.sink_failures` increase over the window.

## AuthSecurityHistoryNotRecording

**Meaning:** account security history entries are being dropped because the queue is full, failing to reach the `security_history_outbox`, or failing in the security-history worker. Owners' history and security alerts are incomplete.

### Triage

1. In the **Pipeline health** row, see which signal is rising: `history dropped`, `history store failures`, or worker `entries failed`.
2. Check the outbox, the Debezium connector, and the worker, as described in [deployment](../../DEPLOYMENT.md#account-security-history):

   ```sh
   docker compose logs --since 30m backend security-history-worker | grep -iE 'outbox|history' | tail -50
   curl -s http://localhost:8083/connectors/security-history-outbox/status
   ```

### Contain

Restore the database or the connector. Entries already in the outbox survive and are relayed. Entries that were dropped from the full in-memory queue are gone; the `dropped` counter says how many.

### Verify and close

Record the window, the dropped count, and the cause. Users whose history has a gap do not get a missing security alert re-sent, so if the gap covers password or provider changes, decide whether to notify them.

## AuthTelemetryAbsent

**Meaning:** Prometheus has received no OTLP export from `eventxperience-api` in the environment named by `deployment_environment_name` for about 15 minutes, although that environment exported within the last 7 days. Every other auth alert for that environment is blind. Without an environment label, no API has exported at all in the last 7 days.

### Triage

1. Is the API up? Run `docker compose ps backend` and check the ingress health.
2. Is export configured? `OTEL_EXPORTER_OTLP_ENDPOINT` must be set on the API; see [metrics export](../../CONFIGURATION.md#metrics-export).
3. Can the API reach Prometheus? Look for exporter errors in the API log, and check that Prometheus runs with `--web.enable-otlp-receiver`.

### Contain and close

Restore the API or the export configuration. If the environment was retired on purpose, silence the alert in Alertmanager for 7 days instead. Record the blind window. Then review the event log for that window by hand, using the triage commands in the other runbooks, because no alert could have fired.

## Rehearse

- **Alert logic:** the promtool cases `auth faults ...`, `sink failures ...`, `dropped history entries ...`, and `an API that stopped exporting ...` in `docker/monitoring/prometheus/tests/auth-alerts.test.yml`.
- **End to end:** with the `ops` profile up, stop the API (`docker compose stop backend`) and watch `AuthTelemetryAbsent` go pending, then firing, in Alertmanager after 15 minutes. Start it again and watch it resolve. To rehearse history loss, stop Kafka Connect and run `auth-drill --scenario session-revocation`. The drill's history check fails, which reproduces the symptom, and the outbox drains once Connect is back.
