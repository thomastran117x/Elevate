# Auth incident record template

Copy this template into the **private** incident tracker, never into a public issue or this repository, when an auth alert is acknowledged or a security report needs action. Incident records hold pseudonymous identifiers and operational detail. The [evidence required to close](ownership.md#evidence-required-to-close-an-incident) is listed in ownership.

Record account ids, session counts, and keyed hashes only. Never record email addresses, usernames, client addresses, tokens, codes, links, or secret values.

```markdown
# AUTH-INC-<yyyy>-<nn>: <short title>

| Field | Value |
| --- | --- |
| Opened (UTC) | |
| Trigger | Alert `<name>` (severity, mode) / user report / drill |
| Runbook | <link to the runbook section followed> |
| Auth on-call | |
| Security lead | |
| Incident commander | (if appointed) |
| Status | open / contained / closed |

## Timeline (UTC)

| Time | Event |
| --- | --- |
| | Alert fired (`ALERTS{alertname="..."}` query or dashboard link) |
| | Acknowledged |
| | Containment applied |
| | Signal back to baseline |
| | Closed |

## Scope

- Affected account ids:
- Sessions revoked:
- Source / target hashes:
- Data or credentials possibly exposed:

## Actions

| Time | Action (setting change, disable, revocation, rotation, block) | Undone at / kept because |
| --- | --- | --- |

## Users told

| Account id | Channel | When | What |
| --- | --- | --- | --- |

## Evidence of resolution

<!-- Dashboard panel or query result showing the signal back at baseline. -->

## Follow-up

- [ ] Root-cause issue:
- [ ] Missed-detection or threshold issue:
- [ ] Runbook correction:

## Review

Was the alert's threshold right? Was the runbook enough? (feeds the quarterly review)

Closed by (security lead), date:
```
