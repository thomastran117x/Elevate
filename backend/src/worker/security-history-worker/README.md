# Security history worker

.NET 10 background application that keeps the account security history (#326). The API stores each entry in `security_history_outbox`, and Debezium relays it to the security-history topic. This worker consumes the topic and appends the entries to `AccountSecurityEvents`. It emails the owner about high-risk changes and deletes entries past their 90-day retention. The host registers a scoped writer, message processor, alert runner and retention runner, a DLQ publisher, a Kafka consumer, the alert dispatcher, and the retention purge.

This is the only worker that connects to PostgreSQL. It writes `AccountSecurityEvents` in the API's database, reads the owner's address from `Users` to send an alert, and removes relayed outbox rows. The API owns the schema and its migrations. The worker waits until `AccountSecurityEvents` exists before consuming, so it can start first.

See [security events](../../../../docs/SECURITY_EVENTS.md#account-security-history) for what is recorded and why, the [API README](../../../README.md), [configuration](../../../../docs/CONFIGURATION.md#account-security-history), and [CONTRIBUTING](../../../../CONTRIBUTING.md).

## Dependencies and settings

You need Kafka, the API's PostgreSQL database, and the `security-history-outbox` Debezium connector that relays the API's outbox to the topic. Alerts also need the email worker.

| Variable                                  | Default / purpose                                                                                                                                                                              |
| ----------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `KAFKA_BOOTSTRAP_SERVERS`                 | Shared broker setting. Standalone fallback `localhost:9092`, template/Compose `kafka:9092`                                                                                                     |
| `DB_CONNECTION_STRING`                    | The API's database                                                                                                                                                                             |
| `SECURITY_HISTORY_TOPIC`                  | `eventxperience-security-history`. Entries Debezium relays from the API's outbox; also the connector's route                                                                                   |
| `SECURITY_HISTORY_GROUP_ID`               | `security-history-worker`                                                                                                                                                                      |
| `SECURITY_HISTORY_DLQ_TOPIC`              | `eventxperience-security-history-dlq`                                                                                                                                                          |
| `SECURITY_HISTORY_RETENTION_DAYS`         | `90`, 1–90. Owners are told entries are kept for up to 90 days, so it can only shorten that. Fixed into each entry's `ExpiresAtUtc` when it is stored, so a change applies to new entries only |
| `SECURITY_HISTORY_PURGE_INTERVAL_MINUTES` | `60`, 1–1440                                                                                                                                                                                   |
| `SECURITY_HISTORY_PURGE_BATCH_SIZE`       | `500`, 1–5000 rows per delete                                                                                                                                                                  |
| `SECURITY_HISTORY_PURGE_MAX_BATCHES`      | `20`, 1–1000 batches per pass. Whatever is left waits for the next pass                                                                                                                        |
| `SECURITY_HISTORY_NOTIFICATIONS_ENABLED`  | `true`. Email the owner about alert kinds                                                                                                                                                      |
| `SECURITY_HISTORY_ALERT_KINDS`            | Comma-separated kinds, or `none`. Unset means `password_changed,provider_linked,refresh_token_reuse_detected,account_disabled,recovery_code_used`                                              |
| `SECURITY_HISTORY_ALERT_MAX_AGE_MINUTES`  | `1440` (24 h), 5–10080. A change older than this is recorded but not alerted about                                                                                                             |
| `EMAIL_TOPIC`                             | `eventxperience-email`. Where alerts are handed to the email worker                                                                                                                            |

The worker refuses to start if a value is out of range, names an unknown kind, or lists no kinds at all (only `none` turns alerts off). The default alert kinds are the high-risk changes that send nothing of their own. MFA changes (including recovery-code generation), email changes, and password resets already email the owner when they happen, so adding those kinds sends two emails for one change. A spent recovery code sends nothing directly, so its alert is the owner's only notice. Options come from the environment. Non-container runs load ancestor `.env` files, and process values take precedence.

## Run and Docker

From the repository root, with infrastructure and environment settings ready:

```powershell
dotnet run --project backend/src/worker/security-history-worker/security-history-worker.csproj
docker compose up -d security-history-worker
docker compose logs -f security-history-worker
```

For a standalone image build, also from the root:

```powershell
docker build -f backend/src/worker/security-history-worker/Dockerfile -t eventxperience-security-history-worker:local backend
```

Use `backend/` as the build context, because the worker shares the entity, message contract, and notification service with the API. This is a background consumer with no HTTP API.

## Processing and failures

`SecurityHistoryMessageParser` holds each payload to the producer's contract before anything is stored:

- known kinds, clients, and device classes;
- a recorded catalog event name;
- known method, factor, and reason names;
- a network that is exactly a masked /24 or /48 prefix;
- a well-formed request id.

`SecurityHistoryMessageProcessor` handles the outcomes like this:

- **A new entry** is inserted. If its kind is an alert kind and alerts are on, the same insert marks its alert `Pending`.
- **A redelivery** matches the unique `MessageId` and changes nothing, including the alert state already on the row.
- **An entry for a deleted account** fails the foreign key and is dropped. The history went with the account.
- **An entry already past retention**, for example after a long outage, is skipped.
- **A malformed payload** goes to the DLQ, since retrying cannot fix it.
- **A storage failure** is retried by EF's execution strategy first. If it still fails, the offset stays uncommitted, and the consumer seeks back to the message and tries it again after five seconds. It keeps the same consumer, so it does not leave and rejoin the group. An outage delays the history; it does not send it to the DLQ.
- **A DLQ publish that fails** is retried the same way.
- **A consumer or broker error** closes the consumer, so it leaves the group cleanly and its partitions move at once. A new consumer is built after five seconds.

The consumer starts at the earliest offset when its group has no committed position, and it disables auto-commit. It commits after processing returns, including after a successful DLQ publication.

### Alerts

`SecurityHistoryAlertService` runs `SecurityHistoryAlertRunner` every five seconds, separately from the consumer, so a slow or unavailable email topic delays alerts but never the history. Each pass:

1. Takes up to 20 `Pending` alerts that are due.
2. Claims each by moving its next attempt a minute ahead, so a second replica skips it.
3. Hands it to the email topic, with a 10-second timeout and the host's cancellation.

Each alert's claim and backoff are timed from when that alert is handled, so a slow batch cannot let another replica claim an alert still being sent.

- **A failed send** is retried after 30 seconds, then with the delay doubling up to 30 minutes. After five failed attempts the alert is marked `Failed`.
- **A send that times out** is marked `Unconfirmed` and not retried. The producer still holds the message and may yet deliver it, so a retry could email the owner twice.
- **A stale alert** is marked `Skipped` without a send: the change is older than `SECURITY_HISTORY_ALERT_MAX_AGE_MINUTES` (24 hours by default). This covers entries stored from a backlog after an outage, and alerts left `Pending` while alerts were off.
- **An account with no address** is marked `Failed` without a send. Because the state lives on the row, a crash, a redelivery, or a restart neither loses an alert nor sends it twice; an alert interrupted mid-send is retried once its claim lapses. The owner's address is read from `Users` at send time and never travels on the topic. With `SECURITY_HISTORY_NOTIFICATIONS_ENABLED=false` the dispatcher stays idle and new entries are stored with no alert.

## Retention

`SecurityHistoryRetentionService` runs `SecurityHistoryRetentionRunner` on startup and then every purge interval. Each pass deletes entries whose `ExpiresAtUtc` has passed, oldest first, in bounded batches. Each pass logs how many it deleted when that is more than zero. The API also hides expired entries, so nothing is shown between expiry and the next pass. Each pass also removes `security_history_outbox` rows older than a day. Debezium reads inserts from the WAL its replication slot retains and ignores deletes, so this never loses an entry, even while the connector is behind. Every replica purges; the deletes are idempotent.

## Metrics

The `EventXperience.SecurityHistory` meter has three counters:

- `security_history.entries` by `outcome` (`inserted`, `duplicate`, `account_deleted`, `expired`, `rejected`, `failed`);
- `security_history.alerts` by `outcome` (`sent`, `retried`, `failed`, `unconfirmed`, `skipped`, `no_recipient`);
- `security_history.purged`.

Read them with `dotnet-counters monitor --counters EventXperience.SecurityHistory` until an exporter is chosen (#330). Watch the DLQ, `failed` entries, and `failed` alerts.

## Tests

From the repository root:

```powershell
dotnet test backend.tests.Unit/backend.tests.Unit.csproj --filter "FullyQualifiedName~Workers.SecurityHistory"
```

The unit project references this worker, so `security-history-worker.dll` is measured by the coverage gate. Unit tests drive the consume loop against a mocked consumer, and run the writer, alerts, and purge against SQLite. The integration project references the worker under the `securityhistoryworker` extern alias, because its top-level `Program` would otherwise clash with the API's. `SecurityHistoryPipelineTests` drives real auth journeys through the API, reads the entries the API stores in its outbox (the payload Debezium relays), and runs them through the processor and alert dispatcher against PostgreSQL. Kafka Connect is not part of the test infrastructure; the live Playwright journey in `frontend/tests/account-activity.spec.ts` covers the relay. See [testing](../../../../docs/TESTING.md) for prerequisites and coverage boundaries.
