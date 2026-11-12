# Drill log

Append-only record of auth drills and rehearsals (see [drills](drills.md)). One row per run. Record no credentials, tokens, addresses, or account details. The run id and the request ids in the report are enough to trace a run. Keep the JSON report with the linked record.

| Date (UTC) | Drill | Run id | Environment | Scenarios | Outcome | Record / follow-up |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-11-12 | `auth-drill` | `20261112212958a2aa27` | Local Compose `ops` stack (#330 verification) | all | passed | First run, during #330 |
| 2026-11-12 | Telemetry-loss rehearsal | — | Local Compose `ops` stack (#330 verification) | API stopped 16 minutes | passed | `AuthTelemetryAbsent` fired and routed to the `observe` receiver |
| 2026-11-12 | Key-rotation rehearsal | — | Local Compose `ops` stack (#330 verification) | JWT and TOTP rotate, roll back, retire | passed | Old tokens and TOTP kept working through rotation and rollback; the retired key's token was refused |
| 2026-11-12 | `auth-drill` | `202611122154420e1405` | Local Compose `ops` stack (#330 verification) | all | passed | Final run on the code in #330 |
