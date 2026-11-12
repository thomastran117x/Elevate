---
name: Auth drill record
about: Record an auth drill or rehearsal and attach its report (docs/operations/drills.md)
title: "Auth drill: <yyyy-mm-dd> <scenario>"
labels: security, auth
assignees: ""
---

<!--
Attach the JSON report from .tmp/auth-drill/<run>.json. It holds outcomes, timings, and request
ids only. Do not paste credentials, tokens, links, or codes.
-->

| Field | Value |
| --- | --- |
| Run id | |
| Drill | `auth-drill` / key-rotation rehearsal / telemetry-loss rehearsal |
| Environment | |
| Scenarios | |
| Outcome | passed / failed |
| Run by | |

## Failed steps

<!-- Step name, the expected and actual result from the report, and the request ids. -->

## Follow-up

- [ ] Drill log row added in docs/operations/drill-log.md
- [ ] Defect issue opened for each failed step
