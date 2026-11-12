# Auth security monitoring

Dashboards and alerts for the authentication and authorization security signals in [security events](../SECURITY_EVENTS.md). Everything is code in [`docker/monitoring/`](../../docker/monitoring):

| Path | Contents |
| --- | --- |
| `prometheus/prometheus.yml` | Prometheus with the OTLP receiver and the rule files |
| `prometheus/rules/auth-recording.yml` | Per-environment signal rates and their 14-day baselines |
| `prometheus/rules/auth-alerts.yml` | The alerts, with severity, owner, mode, runbook, and threshold rationale |
| `prometheus/tests/auth-alerts.test.yml` | promtool unit tests that inject representative series |
| `alertmanager/alertmanager.yml` | Routing by mode and severity |
| `grafana/provisioning/`, `grafana/dashboards/auth-security.json` | The Prometheus data source and the Auth security dashboard |

No observability vendor is assumed. The API and workers push standard OTLP, so the same metrics can go to any OTLP-capable backend. The rules and dashboard are plain PromQL.

## Running the stack

The `ops` Compose profile adds Prometheus, Alertmanager, Grafana, and Mailpit to the normal stack. In `.env`:

```dotenv
OTEL_EXPORTER_OTLP_ENDPOINT=http://prometheus:9090/api/v1/otlp
OTEL_RESOURCE_ATTRIBUTES=deployment.environment.name=local
GRAFANA_ADMIN_PASSWORD=<choose one>
# Optional, for drills: catch email in Mailpit instead of sending it.
SMTP_SERVER=mailpit
SMTP_PORT=1025
EMAIL_USER=auth-drill@example.com
EMAIL_PASSWORD=drill
```

Then:

```powershell
docker compose --profile ops up --build -d
```

| UI | URL |
| --- | --- |
| Grafana (Auth security dashboard is the home page) | http://localhost:3000 |
| Prometheus (rules, alerts, `ALERTS`) | http://localhost:9090 |
| Alertmanager | http://localhost:9094 |
| Mailpit | http://localhost:8025 |

The ports bind to localhost only. Grafana has anonymous access and sign-up turned off. For a shared deployment, put all four behind your SSO proxy and restrict access as you would the event log. The metrics carry no identifiers, but the alert history is operational security data.

The API exports `EventXperience.Auth`; the security-history worker `EventXperience.SecurityHistory`; the email and SMS workers `EventXperience.Notifications`. See [metrics export](../CONFIGURATION.md#metrics-export). Prometheus's OTLP receiver renders names as the dashboard and rules expect them:
- dots become underscores;
- counters gain `_total`;
- the millisecond histogram becomes `auth_operation_duration_milliseconds_*`;
- `service.name` becomes `job`;
- `deployment.environment.name` becomes `deployment_environment_name`.

## Dashboard

**Auth security** (uid `auth-security`) has one row per area. Each panel is filtered by the `Environment` variable.

| Row | Shows |
| --- | --- |
| Sign-in outcomes | `auth.sign_in.*` by outcome, reason, and method |
| Account and IP throttles | `auth.throttled` by dimension and flow |
| Recovery and MFA delivery | Recovery requests by reason, security email and SMS deliveries by outcome, MFA delivery throttles |
| Step-up and verification failures | Step-up results and verification failures by reason and flow, recovery codes used |
| Token reuse and session revocation | Refresh-token replays, session revocations by reason and outcome |
| Provider linking | Links, unlinks, and conflicts by provider and reason |
| Authorization denials | `authz.denied` by reason, against the alert thresholds |
| Latency and faults | p50/p95 by operation, outcomes, and the faulted share |
| Pipeline health | Sink failures, history drops and store failures, and the security-history worker |
| Alert history | Every auth alert that was pending or firing, and the current ones |

The dashboard is provisioned read-only, so change the JSON, not the UI. Panels never group by account, session, hash, or address: those exist only in the event log. Each runbook shows how to pull them from there.

## Alerts

| Alert | Severity | Owner | Runbook |
| --- | --- | --- | --- |
| `AuthCredentialStuffing` | page | Auth on-call | [credential stuffing](runbooks/credential-stuffing.md#authcredentialstuffing) |
| `AuthTargetedAccountThrottling` | ticket | Auth on-call | [credential stuffing](runbooks/credential-stuffing.md#authtargetedaccountthrottling) |
| `AuthRefreshTokenReuse` | page | Security lead | [token theft](runbooks/token-theft.md#authrefreshtokenreuse) |
| `AuthSessionRevocationFaulted` | page | Auth on-call | [token theft](runbooks/token-theft.md#authsessionrevocationfaulted) |
| `AuthProviderLinkConflicts` | ticket | Security lead | [provider-link conflict](runbooks/provider-link-conflict.md#authproviderlinkconflicts) |
| `AuthStepUpBruteForce` | page | Auth on-call | [MFA and recovery abuse](runbooks/mfa-recovery-abuse.md#authstepupbruteforce) |
| `AuthMfaRecoveryAbuse` | ticket | Auth on-call | [MFA and recovery abuse](runbooks/mfa-recovery-abuse.md#authmfarecoveryabuse) |
| `AuthMfaDeliveryFailures` | ticket | Auth on-call | [MFA and recovery abuse](runbooks/mfa-recovery-abuse.md#authmfadeliveryfailures) |
| `AuthMassAuthorizationDenial` | page | Auth on-call | [authorization denial](runbooks/authorization-denial.md#authmassauthorizationdenial) |
| `AuthAuthorizationProbing` | ticket | Security lead | [authorization denial](runbooks/authorization-denial.md#authauthorizationprobing) |
| `AuthFaultRate` | page | Auth on-call | [service health](runbooks/auth-service-health.md#authfaultrate) |
| `AuthEventPipelineBroken` | page | Auth on-call | [service health](runbooks/auth-service-health.md#autheventpipelinebroken) |
| `AuthSecurityHistoryNotRecording` | ticket | Auth on-call | [service health](runbooks/auth-service-health.md#authsecurityhistorynotrecording) |
| `AuthTelemetryAbsent` | ticket | Auth on-call | [service health](runbooks/auth-service-health.md#authtelemetryabsent) |

Each alert's `threshold_rationale` annotation explains its threshold in one sentence. Owners and escalation are in [ownership](ownership.md).

## Thresholds

Rate alerts compare the current value with **max(3 × baseline, floor)**:

- **Baseline:** the 99th percentile of the same signal over the previous 14 days, recorded hourly as `<signal>:p99_14d`. It excludes the most recent hour, so an ongoing attack cannot raise its own threshold.
- **3 ×:** keeps ordinary peaks, such as a Monday-morning sign-in rush or a newsletter that triggers resets, below the line.
- **Floor:** an absolute minimum per alert. It covers a new deployment with no history yet, and a quiet environment where 3 × a near-zero baseline would page on a handful of events.

Four alerts deliberately use fixed thresholds instead, each justified in its rationale:
- `AuthFaultRate` uses a 2% error budget, because faults are service errors whatever the traffic;
- `AuthSessionRevocationFaulted`, `AuthEventPipelineBroken`, and `AuthSecurityHistoryNotRecording` fire on any sustained occurrence, because each one is a failed fail-closed control or lost audit data, never an expected refusal;
- `AuthTelemetryAbsent` fires after 15 minutes without export.

## Rollout

Alerts activate in three stages, so paging thresholds come from measured baselines:

1. **Observe (now).** Every alert has `mode="observe"`. Alertmanager routes these to a receiver with no notifications. Firings are still recorded in Prometheus's `ALERTS` series, the dashboard's **Alert history** row, and the Alertmanager UI.
2. **Baseline (at least two weeks).** At the weekly review, open the **Alert history** row and each signal's panel. For each alert, compare how often it fired against what actually happened. Compare its floor with the recorded `p99_14d`. Adjust the floor or the multiplier in a reviewed change, and update the rationale. Run the promtool tests after every change.
3. **Page.** Flip an alert's `mode` to `"page"` in its own reviewed pull request. The description must record the measured baseline and the observe-period firings. Mount the receiver URLs first, as described below. Code owners must approve changes to `docker/monitoring/`.

Paging receivers read their webhook URLs from files, so no URL or token is committed. Mount `page-webhook-url` and `ticket-webhook-url` into the Alertmanager container at `/etc/alertmanager/secrets/` before any alert moves to `mode="page"`. Point them at your paging and ticketing tools' webhook endpoints.

## Testing the rules

CI's **Monitoring Definitions** job runs these on every pull request, using the pinned images:

```powershell
docker run --rm -v "${PWD}/docker/monitoring/prometheus:/etc/prometheus:ro" --entrypoint promtool prom/prometheus:v3.15.1 check config /etc/prometheus/prometheus.yml
docker run --rm -v "${PWD}/docker/monitoring/prometheus:/etc/prometheus:ro" -w /etc/prometheus/tests --entrypoint promtool prom/prometheus:v3.15.1 test rules auth-alerts.test.yml
docker run --rm -v "${PWD}/docker/monitoring/alertmanager:/etc/alertmanager:ro" --entrypoint amtool prom/alertmanager:v0.34.1 check-config /etc/alertmanager/alertmanager.yml
```

From Git Bash on Windows, prefix each command with `MSYS_NO_PATHCONV=1` and use `$(pwd -W)` for the mount.

Each case in `auth-alerts.test.yml` injects representative counter series and asserts on `ALERTS`. The cases are:
- the alert fires on a spike;
- it stays silent below its floor;
- it stays silent at a high but normal learned baseline;
- it ignores neighbouring signals, such as CAPTCHA failures for credential stuffing or source-IP throttles for targeted accounts.

Add a case with every new alert or threshold change.

`backend.tests.Unit/Application/Telemetry/MonitoringDefinitionsTests.cs` checks the definitions against the code. It runs in the unit suite and the Monitoring Definitions job. It requires that:
- every alert has a valid severity, an owner from [ownership](ownership.md), a mode, a rationale, and a `runbook_url` whose file and heading exist;
- every `event`, `reason`, `flow`, and `dimension` value used in the rules and dashboard exists in the security event catalog;
- every required dashboard row exists;
- no rule or panel groups by, or matches on, a label that could carry an identifier.

To see the rules against live data, run the [drill](drills.md) with the `ops` profile up. Its `telemetry` scenario also scans Prometheus for prohibited label names and values.

## Privacy

Monitoring holds aggregates only:
- every tag value comes from a closed set;
- account ids, session ids, hashes, addresses, routes, and correlation ids never become labels;
- delivery metrics never carry recipients or provider errors.

Three checks enforce this:
- the unit tests above;
- the canary-secret tests in [security events](../SECURITY_EVENTS.md#privacy-and-redaction), which cover metric tags;
- the drill's live scan of Prometheus.

The pseudonymous detail stays in the event log, which runbooks query directly.

## Auditing alert outcomes

- **Firings:** Prometheus keeps `ALERTS` and `ALERTS_FOR_STATE` for its 400-day retention. Query `ALERTS{alertname=~"Auth.*"}` over any period, or use the **Alert history** row.
- **Responses:** every page, and every ticket that leads to action, gets an [incident record](incident-record.md), kept in the private tracker. The record links the alert, the evidence, and the closing review.
- **Threshold changes and mode flips** are pull requests to `docker/monitoring/`, which require code-owner review.
- **Drill outcomes** are recorded in the [drill log](drill-log.md).
