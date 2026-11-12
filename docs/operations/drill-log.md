# Drill log

Append-only record of auth drills and rehearsals (see [drills](drills.md)). One row per run. Record no credentials, tokens, addresses, or account details. The run id and the request ids in the report are enough to trace a run. Keep the JSON report with the linked record.

| Date (UTC) | Drill | Run id | Environment | Scenarios | Outcome | Record / follow-up |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-11-12 | `auth-drill` | `20261112212958a2aa27` | Local Compose `ops` stack (#330 verification) | all | passed | First run, during #330 |
