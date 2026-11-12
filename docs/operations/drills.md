# Recovery and session-revocation drills

The drill proves, from the outside, that the controls users depend on in an incident still work:
- ending other sessions;
- detecting refresh-token theft;
- recovering an account;
- getting the signals into monitoring without leaking anything.

It is `auth-drill` in [Event.DevTasks](../../tools/Event.DevTasks/README.md). It runs only against a non-production environment, and it is run by hand on the cadence below.

## Cadence and ownership

| What | When | Who |
| --- | --- | --- |
| `auth-drill --scenario all` | Monthly, and after any change to sessions, tokens, recovery, step-up, or the security event pipeline | Auth on-call |
| [Key-rotation rehearsal](key-rotation.md#rehearsal) | Quarterly, and before any planned production rotation | Security lead |
| Telemetry-loss rehearsal (stop the API, see [service health](runbooks/auth-service-health.md#rehearse)) | Quarterly | Auth on-call |

Missing a drill is recorded in the [quarterly review](review-checklist.md). Roles are defined in [ownership](ownership.md).

## Prerequisites

A non-production stack, typically the Compose stack with the `ops` profile (see [monitoring](monitoring.md#running-the-stack)), with:

- **Email caught by Mailpit:** set `SMTP_SERVER=mailpit`, `SMTP_PORT=1025`, an `EMAIL_USER` that is an email address, and any `EMAIL_PASSWORD`. The drill reads verification links and codes from Mailpit's API.
- **Metrics exported to Prometheus,** for the telemetry scenario: `OTEL_EXPORTER_OTLP_ENDPOINT=http://prometheus:9090/api/v1/otlp`.
- **CAPTCHA bypass:** set `CAPTCHA_ALLOW_BYPASS=true`. It only works outside production.
- **Raised auth limits:** the drill makes more auth requests from one address in a few minutes than the defaults allow. In `.env`, set `RateLimiter__AuthPermitLimit=200` (passed to the API through `env_file`) and `AUTH_ABUSE_SHARED_IP_LIMIT=200`. The per-account login limit stays at its default, which is what the telemetry scenario's wrong-password burst trips.
- **The security history pipeline running:** the API, Kafka, Kafka Connect, and `security-history-worker`. Three scenarios read the account's history.

## Running it

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- auth-drill `
  --base-url http://localhost:8090 `
  --mailpit-url http://localhost:8025 `
  --prometheus-url http://localhost:9090 `
  --environment local
```

It takes about two minutes. `--scenario` runs one scenario. `--environment` must match the `deployment.environment.name` in `OTEL_RESOURCE_ATTRIBUTES`. The drill refuses hosts that do not look non-production (loopback, `*.local`, `*.internal`, or a name containing `dev`, `test`, `staging`, `qa`, `sandbox`, `preview`, or `drill`) unless `--confirm-non-production` is passed. Never point it at production: it creates an account and trips abuse limits.

## What it checks

It signs up a throwaway account (`auth-drill-<run>@example.com`) with a random password, then:

| Scenario | Steps | Proves |
| --- | --- | --- |
| `session-revocation` | Verifies a second device by its emailed link. Steps up on the first device with an emailed code. Signs out every other session. | The other device's access token is refused at once and its refresh token is refused. The first session survives. The history records `other_sessions_revoked`. |
| `token-reuse` | Refreshes, waits past the 10-second grace window, then replays the rotated refresh token. | The replay is refused, and the whole session, including the newest token, is revoked. The history records `refresh_token_reuse_detected`. |
| `recovery` | Requests a password reset and follows the emailed link. | Every session from before the reset is refused, and the new password signs in. The history records `password_reset`. |
| `telemetry` | Calls a `[RequireMfa]` route without a step-up proof. Deletes the account. Sends wrong passwords for an unknown user until throttled. | `MFA_REQUIRED` is enforced. The sign-in failure, throttle, and `authz.denied` counters move in Prometheus. Monitoring holds only allowlisted label names, and no value looks like an email, address, JWT, or long hash, or contains any of the drill's own credentials, tokens, or codes. |

Cleanup steps up and deletes the account. If cleanup fails, the report says so; delete the account by hand.

## Recording the outcome

The drill writes a JSON report (default `.tmp/auth-drill/<run>.json`). It holds every step's outcome, duration, and the API's `X-Request-Id` values, so any step can be traced in the event log. It never contains the account's email, password, tokens, links, or codes. After each drill:

1. Add a row to the [drill log](drill-log.md): the date, run id, environment, scenarios, outcome, and follow-up issue.
2. Attach the report to an issue made from the **Auth drill record** template, or keep it in the private tracker with the incident records.
3. A failed drill is a defect in a security control. Open an issue labelled `security` and treat it like a `ticket` alert. Fix it before the next drill.
