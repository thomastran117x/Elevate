# Ownership and escalation

Auth security operations are owned by roles, not by people. The `owner` label on every alert in `docker/monitoring/prometheus/rules/auth-alerts.yml` is one of the role ids below, and `MonitoringDefinitionsTests` fails on any other value. [`.github/CODEOWNERS`](../../.github/CODEOWNERS) requires the same people to review changes to auth code, monitoring definitions, and these documents.

## Roles

| Role id | Role | Holds it today | Accountable for |
| --- | --- | --- | --- |
| `auth-oncall` | Auth on-call | @thomastran117 | Acknowledging auth alerts, running the runbooks, containing the incident, and opening the incident record |
| `security-lead` | Security lead | @thomastran117 | Alerts that suggest account takeover or token theft, key and secret rotation, incident closure sign-off, and the [quarterly review](review-checklist.md) |
| `incident-commander` | Incident commander | @thomastran117 | Coordinating any incident that needs more than one responder, user communication, and declaring the incident over |

One person holds every role while the project has a single maintainer. When a second responder joins, assign them `auth-oncall` first. Update this table and `.github/CODEOWNERS` in the same change, so ownership stays in reviewed history.

This process sits inside the project's general incident handling and does not replace it. It defines only the auth-specific roles, targets, and evidence.

## Response targets

| Severity | Acknowledge within | First update within | Who is notified once paging is enabled |
| --- | --- | --- | --- |
| `page` | 15 minutes, at any hour | 1 hour | The alert's owner role, through the page receiver |
| `ticket` | 1 business day | 3 business days | The alert's owner role, through the ticket receiver |

While an alert is in `mode="observe"`, see [rollout](monitoring.md#rollout). It notifies nobody, and the owner reviews its firings at the weekly baseline review instead.

## Escalation

1. **Auth on-call** acknowledges and starts the runbook linked from the alert.
2. Escalate to the **security lead** at once if any of these is true:
   - an account was taken over, or a sign-in succeeded during an attack;
   - tokens or keys may have leaked;
   - an administrator account is involved;
   - the runbook's containment did not stop the signal within an hour.
3. The security lead appoints an **incident commander** when more than one responder is needed, users must be told, or data may have been exposed. Personal-data exposure follows the general incident process for notification duties.
4. If nobody acknowledges a `page` within its target, the page receiver re-notifies every hour (`repeat_interval`). Escalate through the general incident process.

## Evidence required to close an incident

An incident is closed by the **security lead**, in its [incident record](incident-record.md), when the record contains:

- the alert or report that opened it, with the firing time and the `ALERTS` query or a dashboard link;
- a timeline of detection, acknowledgement, containment, and resolution, in UTC;
- the scope: affected account ids, session counts, and source or target hashes. Never plaintext emails, addresses, tokens, or codes;
- every containment action, such as setting changes, account disables, revocations, and key rotations, each with when it was undone or why it stays;
- proof that the signal returned to baseline, such as a dashboard panel or query result;
- for any affected user, what they were told and when;
- follow-up issues for root causes, missed detections, and threshold changes;
- a sentence on whether the alert's threshold and runbook were right, which feeds the next [review](review-checklist.md).
