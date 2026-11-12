# Auth security operations

The continuous controls around authentication and authorization: how regressions are caught, how attacks are detected, and how incidents are handled and audited. The contracts they rely on are in [security events](../SECURITY_EVENTS.md) and [API authorization](../API.md).

| Document | Use it to |
| --- | --- |
| [Ownership and escalation](ownership.md) | Find who owns an alert, how fast to respond, when to escalate, and what evidence closes an incident |
| [Monitoring](monitoring.md) | Run the Prometheus, Alertmanager, and Grafana stack, read the dashboard, understand thresholds, and move an alert from observe to paging |
| Runbooks: [credential stuffing](runbooks/credential-stuffing.md), [token theft](runbooks/token-theft.md), [provider-link conflict](runbooks/provider-link-conflict.md), [MFA and recovery abuse](runbooks/mfa-recovery-abuse.md), [authorization denial](runbooks/authorization-denial.md), [service health](runbooks/auth-service-health.md) | Triage, contain, verify, and close each alert, and rehearse it in a controlled environment |
| [Key rotation](key-rotation.md) | Rotate and roll back JWT, TOTP, OAuth, and other secrets without breaking unrelated ones |
| [Drills](drills.md) and the [drill log](drill-log.md) | Run the session-revocation, token-reuse, recovery, and telemetry drills, and record their outcomes |
| [Quarterly review](review-checklist.md) | Review dependencies, configuration, the public surface, alerts, drills, and access |
| [Incident record](incident-record.md) | Record an incident in the private tracker |

## Controls in CI

| Check | Fails when |
| --- | --- |
| **Auth Contract Gate** | An endpoint has no `[Authorize]`/`[AllowAnonymous]` classification. An `[AllowAnonymous]` class overrides an action. The public surface differs from `ReviewedPublicSurface.cs`. The OpenAPI contract drifts from the committed files. See [testing](../TESTING.md#authorization-audit). |
| **Backend Integration Tests (authorization)** | A routed endpoint, including minimal APIs and hubs, is unclassified. A protected endpoint does not challenge anonymous callers. A public endpoint does. |
| **Monitoring Definitions** | promtool rejects the config, rules, or an alert test case. Alertmanager's config is invalid. An alert lacks an owner, severity, mode, runbook heading, or rationale. A rule or panel matches a value the code cannot emit, or groups by an identifying label. |

Changes to auth code, `docker/monitoring/`, and these documents need review from the owners in [`.github/CODEOWNERS`](../../.github/CODEOWNERS).
