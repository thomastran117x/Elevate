# Quarterly auth security review

Once a quarter, the security lead works through this checklist and records it as an issue from the **Auth security review** template. That issue is the audit trail: what was checked, what was found, and the follow-ups opened. Also run the review after a major auth change or a `page` incident.

Owner: **security lead** (see [ownership](ownership.md)). Cadence: January, April, July, October.

## Dependencies

- [ ] `dotnet restore backend.sln -p:NuGetAudit=true -p:NuGetAuditLevel=low` reports no unreviewed advisory for an auth-relevant package. That covers `Fido2`, `System.IdentityModel.Tokens.Jwt`, `Microsoft.IdentityModel.*`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Otp.NET`, `Konscious.Security.Cryptography.Argon2`, `BCrypt.Net-Next`, `Google.Apis.Auth`, `MailKit`, `StackExchange.Redis`, and `OpenTelemetry.*`. CI only fails on high and critical.
- [ ] No Dependabot pull request for those packages, or for the `frontend` auth stack, has been open for more than 30 days.
- [ ] The pinned monitoring images (`prom/prometheus`, `prom/alertmanager`, `grafana/grafana`, `axllent/mailpit`) have no outstanding security release. Bump them in a reviewed change and rerun the promtool tests.
- [ ] Each [ADR](../adr/README.md) decision trigger has been checked, such as the triggers that reopen the identity-provider decision.

## Configuration

- [ ] Production starts with no fallback secrets: startup validation passes and no "fallback value" error is logged.
- [ ] Key ages against the rotation policy in [key rotation](key-rotation.md). `JWT_SECRET_ACCESS_PREVIOUS` and `AUTH_TOTP_ENCRYPTION_KEYS_PREVIOUS` are empty unless a rotation is under way.
- [ ] Rate limits and abuse budgets (`RateLimiter__*`, `Auth__AbuseProtection__*`) are at their reviewed values, and none was left raised after an incident or a drill.
- [ ] `CAPTCHA_ALLOW_BYPASS` and the seed-account bypass are off in production.
- [ ] Trusted proxies, forwarded headers, CORS origins, and allowed hosts match the current ingress.
- [ ] The step-up proof lifetime (`Auth__StepUp__ProofLifetimeMinutes`) is still 10 or less.
- [ ] The passkey relying party ID and origins match the public site.
- [ ] Metrics export is on in production (`OTEL_EXPORTER_OTLP_ENDPOINT`), and `AuthTelemetryAbsent` has not fired.

## Authorization surface

- [ ] Every change to `backend.tests.Unit/Application/Security/ReviewedPublicSurface.cs` since the last review was intended. Run `git log --since=<last review> -- backend.tests.Unit/Application/Security/ReviewedPublicSurface.cs`.
- [ ] The Auth Contract Gate and the `authorization` integration shard passed on `main` for the whole quarter.
- [ ] Resource authorization rules match the role and ownership matrix in [API](../API.md).

## Monitoring and response

- [ ] For each alert: firings this quarter (`ALERTS{alertname=~"Auth.*"}`), the recorded `p99_14d` baseline, and whether the floor and multiplier are still right. Propose threshold changes, or a `mode="page"` flip after a full observe period, as reviewed pull requests ([rollout](monitoring.md#rollout)).
- [ ] Every `page` incident has a closed [incident record](incident-record.md) with its evidence.
- [ ] Monthly drills ran and passed, and failures were fixed ([drill log](drill-log.md)). The quarterly key-rotation and telemetry-loss rehearsals ran.
- [ ] Access to Grafana, Prometheus, Alertmanager, and the event log is limited to people who hold a role in [ownership](ownership.md).
- [ ] `docs/operations/ownership.md` and `.github/CODEOWNERS` name the current role holders.

## Privacy

- [ ] The drill's Prometheus privacy scan passed in the latest drill.
- [ ] Log retention matches [security events](../SECURITY_EVENTS.md#retention): 30 days searchable, 90 days archived at most.
- [ ] **Known gaps, until they are fixed:**
  - API request logging (`UseSerilogRequestLogging` in `backend/src/main/Program.cs`) records the request path and the raw User-Agent. Some paths carry identifiers or tokens, such as usernames in `/api/profile/{username}`.
  - The email and SMS workers log the recipient address or phone number when a delivery fails.

  Check whether the follow-up issues are resolved. Until they are, restrict access to API and worker logs as you would the event log.
